using System.Net.WebSockets;
using Coconut.Audio;
using Coconut.Client;
using Coconut.Client.Voice;
using Coconut.Media.Native;
using Coconut.Signaling.Contracts;

namespace Coconut.App.Core.Services;

/// <summary>Participação real: sinalização + malha P2P + voz. Uma instância por tentativa de entrada.</summary>
public sealed class CallBackend : ICallBackend
{
    private static readonly TimeSpan AdmissionWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(5);

    private readonly MeshSession _session;
    private readonly VoiceEngine _voice;
    private readonly Coconut.Client.Screen.ScreenShareController _screen;
    private readonly TaskCompletionSource<JoinOutcome> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AudioDeviceCatalog _catalog;
    private readonly IDiagnosticLog _log;
    private readonly CancellationTokenSource _stopStats = new();
    private int _admitted;

    public CallBackend(AudioDeviceCatalog catalog, RtcPeerOptions rtcOptions, IDiagnosticLog? log = null)
    {
        _catalog = catalog;
        _log = log ?? NullDiagnosticLog.Instance;
        _session = new MeshSession(rtcOptions);
        _screen = new Coconut.Client.Screen.ScreenShareController(_session);
        _screen.Problem += message => _log.Write($"tela: {message}");
        _voice = new VoiceEngine(_session, catalog);
        _voice.Problem += message =>
        {
            _log.Write($"aviso ao usuário: {message}");
            Problem?.Invoke(message);
        };
        _voice.Diagnostic += message => _log.Write($"voz: {message}");
        _session.Diagnostic += message => _log.Write($"malha: {message}");
        _session.LinkVerified += (peer, after) => _log.Write($"enlace com {peer} verificado em {after.TotalMilliseconds:0} ms");
        _session.MessageHandled += OnMessage;
        _session.SignalingConnectivityChanged += connected =>
        {
            if (_admitted == 1) { Problem?.Invoke(connected ? "Conexão com o servidor retomada." : "Reconectando ao servidor. O áudio direto pode continuar; a tela será retomada após confirmar a sessão."); Changed?.Invoke(); }
        };
        _session.Admitted += OnAdmitted;
        _session.AdmissionRejected += failure =>
        {
            _log.Write($"admissão recusada: {failure.Reason}");
            AdmissionFailureDescription = failure.Reason == AdmissionFailure.RoomEmptied ? "A sala ficou vazia e foi encerrada enquanto você entrava. Peça um convite novo."
                : "Não foi possível conectar diretamente a todos. " + string.Join("; ", failure.FailedPeers.Select(p => $"{p.DisplayName}: {(p.State == ParticipantState.Reconnecting ? "reconectando" : "enlace P2P não confirmado")}")) + ". Tente novamente; algumas redes bloqueiam P2P.";
            _outcome.TrySetResult(JoinOutcome.AdmissionFailed);
        };
        _session.Ended += reason =>
        {
            _log.Write($"sessão encerrada: {reason}");
            if (!_outcome.TrySetResult(JoinOutcome.ServerUnavailable))
            {
                Ended?.Invoke(reason);
            }
        };
    }

    public event Action? Changed;

    public Coconut.Client.Screen.IScreenSharing ScreenSharing => _screen;
    public string? AdmissionFailureDescription { get; private set; }
    public string ConnectionSummary => _session.IsSignalingConnected ? "Sinalização conectada · mídia direta entre os participantes" : "Reconectando a sinalização · a tela está pausada";
    public IReadOnlyDictionary<ParticipantId, string> PeerConnectionStates
    {
        get
        {
            var stats = _voice.Receiver.Stats;
            var decoded = _voice.Receiver.DecodedPeers;
            return Roster.Where(p => p.Id != You).ToDictionary(p => p.Id, p => !_session.IsPeerVerified(p.Id) ? "Reconectando P2P…"
                : decoded.Contains(p.Id) ? "P2P · áudio decodificado" : stats.TryGetValue(p.Id, out var audio) && audio.Received > 0 ? "P2P · pacotes recebidos" : "P2P · aguardando áudio");
        }
    }

    public event Action<string>? Problem;

    public event Action<string>? Ended;

    public string? InviteCode => _session.InviteCode;

    public ParticipantId? You => _session.You;

    public ParticipantId? Admin => _session.Admin;

    public IReadOnlyList<PeerInfo> Roster => _session.Roster;

    public bool IsMuted
    {
        get => _voice.IsMuted;
        set => _voice.IsMuted = value;
    }

    public double MicrophoneLevel => _voice.MicrophoneLevel;

    public IReadOnlyDictionary<ParticipantId, double> SpeakingLevels => _voice.Receiver.Levels;

    public Task<JoinOutcome> CreateRoomAsync(Uri server, string displayName, CancellationToken cancellation) =>
        ConnectAsync(() => _session.CreateRoomAsync(server, displayName, cancellation), cancellation);

    public Task<JoinOutcome> JoinRoomAsync(Uri server, string inviteCode, string displayName, CancellationToken cancellation) =>
        ConnectAsync(() => _session.JoinRoomAsync(server, inviteCode, displayName, cancellation), cancellation);

    public Task LeaveAsync() => _session.LeaveAsync();

    public Task KickAsync(ParticipantId target) => _session.KickAsync(target);

    public Task EndRoomForAllAsync() => _session.EndRoomForAllAsync();

    public IReadOnlyList<AudioDeviceInfo> Devices(AudioDirection direction) => _catalog.List(direction);

    public string? SelectedDevice(AudioDirection direction) =>
        direction == AudioDirection.Capture ? _voice.SelectedCaptureId : _voice.SelectedRenderId;

    public Task SelectDeviceAsync(AudioDirection direction, string? deviceId) =>
        direction == AudioDirection.Capture ? _voice.SetCaptureDeviceAsync(deviceId) : _voice.SetRenderDeviceAsync(deviceId);

    public async ValueTask DisposeAsync()
    {
        await _stopStats.CancelAsync();
        await _screen.DisposeAsync();
        await _voice.DisposeAsync();
        await _session.DisposeAsync();
        _stopStats.Dispose();
    }

    private async Task<JoinOutcome> ConnectAsync(Func<Task> connect, CancellationToken cancellation)
    {
        try
        {
            await connect();
        }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or ArgumentException or OperationCanceledException)
        {
            return cancellation.IsCancellationRequested ? JoinOutcome.Canceled : JoinOutcome.ServerUnavailable;
        }

        try
        {
            return await _outcome.Task.WaitAsync(AdmissionWait, cancellation);
        }
        catch (TimeoutException)
        {
            return JoinOutcome.AdmissionFailed;
        }
        catch (OperationCanceledException)
        {
            return JoinOutcome.Canceled;
        }
    }

    private void OnAdmitted()
    {
        if (Interlocked.Exchange(ref _admitted, 1) == 0)
        {
            _log.Write($"admitido; participantes: {_session.Roster.Count}");

            // Áudio começa só depois da admissão; falha de dispositivo não derruba a chamada, mas
            // nunca some em silêncio (antes, uma exceção aqui deixava a chamada muda sem aviso).
            _ = Task.Run(async () =>
            {
                try
                {
                    await _voice.StartAsync();
                }
                catch (Exception ex)
                {
                    _log.Write($"voz: falha ao iniciar: {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");
                    Problem?.Invoke($"Não foi possível iniciar o áudio (código 0x{ex.HResult:X8}).");
                }

                Changed?.Invoke();
            });
            _ = Task.Run(() => LogStatsAsync(_stopStats.Token));
            _outcome.TrySetResult(JoinOutcome.Admitted);
        }
    }

    /// <summary>
    /// A cada 5 s: o que saiu do microfone, o que a saída tocou e o que chegou de cada par. Responde
    /// "onde o áudio parou" numa chamada real sem gravar conteúdo nem endereços.
    /// </summary>
    private async Task LogStatsAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(StatsInterval);
        var connectionsLogged = new HashSet<ParticipantId>();
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var sent = _voice.TransmitStats;
                var received = _voice.Receiver.Stats;
                var peers = received.Count == 0
                    ? "nenhum pacote de ninguém"
                    : string.Join("; ", received.Select(p =>
                        $"{p.Key}: pacotes {p.Value.Received}, atrasados {p.Value.Late}, descartados {p.Value.Dropped}, " +
                        $"ocultados {p.Value.Concealed}, faltas {p.Value.Underruns}"));
                _log.Write(
                    $"estatísticas: microfone capturou {sent.FramesCaptured}, codificou {sent.FramesEncoded}, mudo {sent.FramesSuppressedByMute}, " +
                    $"entregas {sent.Deliveries}, nível {_voice.MicrophoneLevel:0}; saída tocou {_voice.Receiver.FramesProduced} quadros " +
                    $"({(_voice.PlaybackInfo is null ? "saída FECHADA" : "saída aberta")}, {(_voice.CaptureInfo is null ? "microfone FECHADO" : "microfone aberto")}); " +
                    $"pares na sala {_session.Roster.Count - 1}; recebido: {peers}");
                if (_screen.Session is not null) { _log.Write($"tela: codificados {_screen.EncodedFrames}, bytes entregues {_screen.DeliveredBytes}, recebidos {_screen.ReceivedFrames}, decodificados {_screen.DecodedFrames}"); }

                // Tipo de conexão (host/srflx/prflx) por par, uma vez por par: diz se passou por NAT.
                if (_session.Roster.Any(p => p.Id != _session.You && !connectionsLogged.Contains(p.Id)))
                {
                    foreach (var (peer, types) in await _session.GetSelectedCandidateTypesAsync())
                    {
                        if (types.Local != "unknown" && connectionsLogged.Add(peer))
                        {
                            _log.Write($"conexão com {peer}: local {types.Local}, remoto {types.Remote}");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Write($"estatísticas interrompidas: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnMessage(ServerMessage message)
    {
        switch (message)
        {
            case InviteInvalid:
                _outcome.TrySetResult(JoinOutcome.InviteInvalid);
                break;
            case RoomFull:
                _outcome.TrySetResult(JoinOutcome.RoomFull);
                break;
            case CommandRejected { Command: "createRoom" or "joinRoom", Reason: "InvalidDisplayName" }:
                _outcome.TrySetResult(JoinOutcome.InvalidName);
                break;
            case CommandRejected { Command: "createRoom", Reason: "RoomLimit" }:
                _outcome.TrySetResult(JoinOutcome.RoomLimit);
                break;
        }

        if (_admitted == 1)
        {
            Changed?.Invoke();
        }
    }
}
