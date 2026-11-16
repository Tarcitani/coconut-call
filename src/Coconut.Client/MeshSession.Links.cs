using Coconut.Media.Native;
using Coconut.Media.Native.Interop;
using Coconut.Signaling.Contracts;

namespace Coconut.Client;

/// <summary>
/// Ciclo de vida dos enlaces P2P: criação pelo papel indicado pelo servidor, aplicação do relay,
/// verificação por eco e renegociação quando um enlace cai antes de ser verificado.
/// </summary>
public sealed partial class MeshSession
{
    /// <summary>Tentativas de negociação por par antes de reportar falha ao servidor.</summary>
    private const int MaxAttempts = 3;

    private readonly Dictionary<ParticipantId, NegotiationRole> _expected = [];
    private readonly HashSet<ParticipantId> _departed = [];

    /// <summary>O servidor pediu um enlace com este par, no papel indicado.</summary>
    private void Expect(ParticipantId remote, NegotiationRole role)
    {
        if (_departed.Contains(remote) || !_expected.TryAdd(remote, role))
        {
            return;
        }

        CreateLink(remote, role, attempt: 1);
    }

    /// <summary>O par saiu ou foi descartado: enlace fechado e nunca recriado.</summary>
    private void Depart(ParticipantId remote)
    {
        _departed.Add(remote);
        _expected.Remove(remote);
        RemoveLink(remote);
    }

    private bool IsCurrent(PeerLink link) => ReferenceEquals(_links.GetValueOrDefault(link.Remote), link);

    private void CreateLink(ParticipantId remote, NegotiationRole role, int attempt)
    {
        var rtc = new RtcPeer(_rtcOptions);
        var link = new PeerLink(remote, role, rtc, attempt);
        _links[remote] = link;

        // Eventos de uma conexão substituída são ignorados (IsCurrent), inclusive descrições e candidatos.
        rtc.LocalDescriptionReady += (type, sdp) => Post(() =>
        {
            if (!IsCurrent(link))
            {
                return Task.CompletedTask;
            }

            link.DescriptionsSent++;
            return _signaling.SendAsync(new Relay(remote, type == "offer" ? RelayKind.Offer : RelayKind.Answer, sdp));
        });
        rtc.LocalCandidateReady += (candidate, mid) => Post(() => IsCurrent(link)
            ? _signaling.SendAsync(new Relay(remote, RelayKind.Candidate, (mid ?? string.Empty) + "\n" + candidate))
            : Task.CompletedTask);
        rtc.StateChanged += state => Post(() => OnStateAsync(link, state));
        rtc.DataChannelOpened += () => Post(() =>
        {
            if (IsCurrent(link))
            {
                link.Peer.SendText(ProbePrefix + link.Nonce);
            }

            return Task.CompletedTask;
        });
        rtc.DataMessageReceived += text => Post(() => OnDataAsync(link, text));
        rtc.AudioPacketReceived += packet => AudioReceived?.Invoke(remote, packet);
        rtc.CallbackFaulted += ex => Diagnostic?.Invoke($"callback nativo: {ex.Message}");

        // A trilha precisa existir antes de qualquer descrição para casar pelo mid nos dois lados.
        rtc.AddAudioTrack((uint)Random.Shared.NextInt64(1, uint.MaxValue), "coconut");
        if (role == NegotiationRole.Offerer)
        {
            rtc.CreateDataChannel("coconut");
            rtc.SetLocalDescription("offer");
        }
    }

    private void ApplyRelay(Relayed relay)
    {
        if (!_links.TryGetValue(relay.From, out var link))
        {
            Diagnostic?.Invoke("relay de par sem enlace (ignorado)");
            return;
        }

        // Nova oferta para um enlace que já negociou = o ofertante renegociou após queda: recomeça do zero.
        if (relay.Kind == RelayKind.Offer && link.RemoteDescriptionSet && !link.Verified)
        {
            Diagnostic?.Invoke($"renegociação recebida (tentativa anterior {link.Attempt}: {link.Timeline})");
            RemoveLink(relay.From);
            CreateLink(relay.From, NegotiationRole.Answerer, link.Attempt + 1);
            link = _links[relay.From];
        }

        IReadOnlyList<string> failures = [];
        switch (relay.Kind)
        {
            case RelayKind.Offer:
                failures = link.ApplyRemoteDescription(relay.Payload, "offer");
                link.Peer.SetLocalDescription("answer"); // sempre, mesmo que algum candidato retido tenha sido rejeitado
                break;
            case RelayKind.Answer:
                failures = link.ApplyRemoteDescription(relay.Payload, "answer");
                break;
            case RelayKind.Candidate:
                var separator = relay.Payload.IndexOf('\n', StringComparison.Ordinal);
                if (separator >= 0)
                {
                    var mid = relay.Payload[..separator];
                    if (link.AddRemoteCandidate(relay.Payload[(separator + 1)..], mid.Length == 0 ? null : mid) is { } failure)
                    {
                        failures = [failure];
                    }
                }

                break;
        }

        foreach (var failure in failures)
        {
            Diagnostic?.Invoke(failure);
        }
    }

    private async Task OnStateAsync(PeerLink link, RtcState state)
    {
        if (!IsCurrent(link))
        {
            return;
        }

        link.RecordState(state);
        if (link.Verified || state is not (RtcState.Disconnected or RtcState.Failed or RtcState.Closed))
        {
            return;
        }

        Diagnostic?.Invoke($"enlace caiu antes de verificar (papel {link.Role}, tentativa {link.Attempt}, " +
            $"descrições env/rec {link.DescriptionsSent}/{link.DescriptionsReceived}, candidatos aplicados {link.CandidatesApplied}, estados {link.Timeline})");

        if (link.Role != NegotiationRole.Offerer)
        {
            return; // o respondente aguarda a renegociação do ofertante
        }

        RemoveLink(link.Remote);
        if (link.Attempt < MaxAttempts)
        {
            var remote = link.Remote;
            var next = link.Attempt + 1;
            _ = Task.Delay(TimeSpan.FromMilliseconds(100 * link.Attempt)).ContinueWith(
                _ => Post(() =>
                {
                    if (_expected.ContainsKey(remote) && !_links.ContainsKey(remote))
                    {
                        CreateLink(remote, NegotiationRole.Offerer, next);
                    }

                    return Task.CompletedTask;
                }),
                TaskScheduler.Default);
        }
        else
        {
            await _signaling.SendAsync(new ReportLink(link.Remote, LinkStatus.Failed));
        }
    }

    private async Task OnDataAsync(PeerLink link, string text)
    {
        if (!IsCurrent(link))
        {
            return;
        }

        if (text.StartsWith(ProbePrefix, StringComparison.Ordinal))
        {
            link.Peer.SendText(ProbeAckPrefix + text[ProbePrefix.Length..]);
        }
        else if (text == ProbeAckPrefix + link.Nonce && !link.Verified)
        {
            link.MarkVerified();
            link.Reported = true;
            RebuildAudioTargets();
            if (link.Attempt > 1)
            {
                Diagnostic?.Invoke($"enlace verificado na tentativa {link.Attempt}");
            }

            LinkVerified?.Invoke(link.Remote, link.VerifiedAfter!.Value);
            await _signaling.SendAsync(new ReportLink(link.Remote, LinkStatus.Verified));
        }
    }

    private void RemoveLink(ParticipantId remote)
    {
        if (_links.Remove(remote, out var link))
        {
            RebuildAudioTargets();
            link.Dispose();
        }
    }

    private void RebuildAudioTargets() => _audioTargets = [.. _links.Values.Where(l => l.Verified)];
}
