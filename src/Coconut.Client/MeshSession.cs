using System.Threading.Channels;
using Coconut.Media.Native;
using Coconut.Media.Native.Interop;
using Coconut.Signaling.Contracts;

namespace Coconut.Client;

public delegate void PeerAudioHandler(ParticipantId from, ReadOnlySpan<byte> rtpPacket);

/// <summary>
/// Participação numa sala: segue as instruções do servidor (connectToPeer, relay, admissão),
/// mantém uma conexão P2P por par e confirma cada enlace pelo eco no canal de dados.
/// Todo estado é alterado num único laço; o áudio recebido sai direto da thread nativa.
/// </summary>
public sealed partial class MeshSession : IAsyncDisposable
{
    private const string ProbePrefix = "probe:";
    private const string ProbeAckPrefix = "probe-ack:";

    private readonly Channel<Func<Task>> _loop = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<ParticipantId, PeerLink> _links = [];
    private readonly SignalingConnection _signaling = new();
    private readonly RtcPeerOptions _rtcOptions;
    private readonly Task _loopTask;
    private volatile PeerLink[] _audioTargets = [];

    public MeshSession(RtcPeerOptions rtcOptions)
    {
        _rtcOptions = rtcOptions;
        _signaling.MessageReceived += message => Post(() => HandleAsync(message));
        _signaling.Closed += _ => Post(() =>
        {
            Ended?.Invoke("signaling-closed");
            return Task.CompletedTask;
        });
        _loopTask = Task.Run(RunLoopAsync);
    }

    public ParticipantId? You { get; private set; }

    public string? InviteCode { get; private set; }

    public bool IsAdmitted { get; private set; }

    public long Epoch { get; private set; }

    public IReadOnlyList<PeerInfo> Roster { get; private set; } = [];

    public event Action? Admitted;

    public event Action<AdmissionFailed>? AdmissionRejected;

    public event Action<ParticipantId, TimeSpan>? LinkVerified;

    public event Action<string>? Ended;

    public event Action<string>? Diagnostic;

    public event PeerAudioHandler? AudioReceived;

    /// <summary>Toda mensagem do servidor, depois de aplicada (observabilidade; contém só IDs e estados).</summary>
    public event Action<ServerMessage>? MessageHandled;

    /// <summary>Tipos de candidato selecionados por par (diagnóstico; sem endereços). Lido dentro do laço.</summary>
    public async Task<IReadOnlyDictionary<ParticipantId, (string Local, string Remote)>> GetSelectedCandidateTypesAsync()
    {
        IReadOnlyDictionary<ParticipantId, (string, string)> result = new Dictionary<ParticipantId, (string, string)>();
        await Post(() =>
        {
            result = _links.ToDictionary(l => l.Key, l => l.Value.Peer.SelectedCandidateTypes());
            return Task.CompletedTask;
        });
        return result;
    }

    public async Task CreateRoomAsync(Uri server, string displayName, CancellationToken cancellation)
    {
        await _signaling.ConnectAsync(server, cancellation);
        await _signaling.SendAsync(new CreateRoom(displayName));
    }

    public async Task JoinRoomAsync(Uri server, string inviteCode, string displayName, CancellationToken cancellation)
    {
        await _signaling.ConnectAsync(server, cancellation);
        await _signaling.SendAsync(new JoinRoom(inviteCode, displayName));
    }

    /// <summary>Envia um quadro Opus já codificado a todos os pares verificados (codifica uma vez, envia N).</summary>
    public int SendAudio(ReadOnlySpan<byte> opusFrame, uint rtpTimestamp)
    {
        var sent = 0;
        foreach (var link in _audioTargets)
        {
            if (link.Peer.SendAudioFrame(opusFrame, rtpTimestamp))
            {
                sent++;
            }
        }

        return sent;
    }

    public Task LeaveAsync() => _signaling.SendAsync(new LeaveRoom());

    public async ValueTask DisposeAsync()
    {
        await _signaling.DisposeAsync();
        await Post(() =>
        {
            foreach (var link in _links.Values)
            {
                link.Dispose();
            }

            _links.Clear();
            _audioTargets = [];
            return Task.CompletedTask;
        });
        _loop.Writer.TryComplete();
        await _loopTask;
    }

    private Task Post(Func<Task> work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_loop.Writer.TryWrite(async () =>
        {
            try
            {
                await work();
            }
            finally
            {
                done.TrySetResult();
            }
        }))
        {
            done.TrySetResult();
        }

        return done.Task;
    }

    private async Task RunLoopAsync()
    {
        await foreach (var work in _loop.Reader.ReadAllAsync())
        {
            try
            {
                await work();
            }
#pragma warning disable CA1031 // Uma falha num evento não pode parar a sessão; é reportada como diagnóstico.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Diagnostic?.Invoke($"falha no laço: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task HandleAsync(ServerMessage message)
    {
        switch (message)
        {
            case SessionEstablished m:
                You = m.You;
                InviteCode = m.InviteCode;
                break;
            case AdmissionStarted m:
                foreach (var peer in m.PeersToConnect)
                {
                    Expect(peer.Id, NegotiationRole.Offerer);
                }

                break;
            case ConnectToPeer m:
                Expect(m.Peer.Id, m.Role);
                break;
            case Relayed m:
                ApplyRelay(m);
                break;
            case PeerAdmitted m:
                Epoch = m.Epoch;
                Roster = m.Roster;
                if (m.Admitted == You && !IsAdmitted)
                {
                    IsAdmitted = true;
                    Admitted?.Invoke();
                }

                break;
            case RoomSnapshot m:
                Epoch = m.Epoch;
                Roster = m.Roster;
                break;
            case PeerLeft m:
                Epoch = m.Epoch;
                Roster = [.. Roster.Where(p => p.Id != m.Peer)];
                Depart(m.Peer);
                break;
            case DropPeer m:
                Depart(m.Peer);
                break;
            case AdmissionFailed m:
                AdmissionRejected?.Invoke(m);
                Ended?.Invoke("admission-failed");
                break;
            case Kicked:
                Ended?.Invoke("kicked");
                break;
            case RoomEnded:
                Ended?.Invoke("room-ended");
                break;
            case CommandRejected m:
                Diagnostic?.Invoke($"comando recusado: {m.Command} ({m.Reason})");
                break;
        }

        MessageHandled?.Invoke(message);
        await Task.CompletedTask;
    }
}
