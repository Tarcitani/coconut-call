using System.Diagnostics;
using Coconut.Media.Native;
using Coconut.Media.Native.Interop;
using Coconut.Signaling.Contracts;

namespace Coconut.Client;

/// <summary>Estado de negociação de uma conexão P2P com um par (alterado só no laço da sessão).</summary>
internal sealed class PeerLink(ParticipantId remote, NegotiationRole role, RtcPeer peer, int attempt) : IDisposable
{
    private readonly List<(string Candidate, string? Mid)> _pendingCandidates = [];
    private readonly List<string> _timeline = [];
    private readonly long _createdAt = Stopwatch.GetTimestamp();

    /// <summary>1 na primeira negociação; cresce a cada renegociação após queda antes da verificação.</summary>
    public int Attempt { get; } = attempt;

    public int DescriptionsSent { get; set; }

    public int DescriptionsReceived { get; private set; }

    /// <summary>Estados com tempo desde a criação (ex.: "Connecting@4", "Disconnected@53"). Sem endereços.</summary>
    public string Timeline => string.Join(',', _timeline);

    public void RecordState(RtcState state) => _timeline.Add($"{state}@{Age.TotalMilliseconds:0}");

    public ParticipantId Remote { get; } = remote;

    public NegotiationRole Role { get; } = role;

    public RtcPeer Peer { get; } = peer;

    /// <summary>Desafio enviado pelo canal de dados; o eco confirma o enlace (protocolo §2, nível 2).</summary>
    public string Nonce { get; } = Guid.NewGuid().ToString("N");

    public bool RemoteDescriptionSet { get; private set; }

    public bool Verified { get; private set; }

    public bool Reported { get; set; }

    public TimeSpan? VerifiedAfter { get; private set; }

    public TimeSpan Age => Stopwatch.GetElapsedTime(_createdAt);

    public int CandidatesApplied { get; private set; }

    /// <summary>
    /// Aplica a descrição remota e então os candidatos retidos. Cada candidato é independente:
    /// um rejeitado nunca impede os demais (antes, uma falha descartava o resto e, no respondente, a resposta).
    /// </summary>
    public IReadOnlyList<string> ApplyRemoteDescription(string sdp, string type)
    {
        Peer.SetRemoteDescription(sdp, type);
        RemoteDescriptionSet = true;
        DescriptionsReceived++;
        var failures = new List<string>();
        foreach (var (candidate, mid) in _pendingCandidates)
        {
            if (TryApply(candidate, mid) is { } failure)
            {
                failures.Add(failure);
            }
        }

        _pendingCandidates.Clear();
        return failures;
    }

    /// <summary>Candidatos que chegam antes da descrição remota ficam retidos. Devolve a falha, se houver.</summary>
    public string? AddRemoteCandidate(string candidate, string? mid)
    {
        if (RemoteDescriptionSet)
        {
            return TryApply(candidate, mid);
        }

        _pendingCandidates.Add((candidate, mid));
        return null;
    }

    public void MarkVerified()
    {
        Verified = true;
        VerifiedAfter = Stopwatch.GetElapsedTime(_createdAt);
    }

    public void MarkUnverified() { Verified = false; Reported = false; }

    public void Dispose() => Peer.Dispose();

    /// <summary>Descrição do candidato sem endereço: tipo, transporte e mid.</summary>
    internal static string Describe(string candidate, string? mid)
    {
        var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var typeIndex = Array.IndexOf(parts, "typ");
        var type = typeIndex >= 0 && typeIndex + 1 < parts.Length ? parts[typeIndex + 1] : "?";
        var transport = parts.Length > 2 ? parts[2].ToLowerInvariant() : "?";
        return $"{type}/{transport} mid={mid ?? "∅"}";
    }

    private string? TryApply(string candidate, string? mid)
    {
        try
        {
            Peer.AddRemoteCandidate(candidate, mid);
            CandidatesApplied++;
            return null;
        }
        catch (RtcException ex)
        {
            return $"candidato rejeitado ({Describe(candidate, mid)}; enlace {Age.TotalMilliseconds:0} ms, estado {Peer.State}): {ex.Message}";
        }
    }
}
