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

    private readonly MeshSession _session;
    private readonly VoiceEngine _voice;
    private readonly TaskCompletionSource<JoinOutcome> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly AudioDeviceCatalog _catalog;
    private int _admitted;

    public CallBackend(AudioDeviceCatalog catalog, RtcPeerOptions rtcOptions)
    {
        _catalog = catalog;
        _session = new MeshSession(rtcOptions);
        _voice = new VoiceEngine(_session, catalog);
        _voice.Problem += message => Problem?.Invoke(message);
        _session.MessageHandled += OnMessage;
        _session.Admitted += OnAdmitted;
        _session.AdmissionRejected += _ => _outcome.TrySetResult(JoinOutcome.AdmissionFailed);
        _session.Ended += reason =>
        {
            if (!_outcome.TrySetResult(JoinOutcome.ServerUnavailable))
            {
                Ended?.Invoke(reason);
            }
        };
    }

    public event Action? Changed;

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
        await _voice.DisposeAsync();
        await _session.DisposeAsync();
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
            // Áudio começa só depois da admissão; falha de dispositivo não derruba a chamada.
            _ = Task.Run(async () =>
            {
                await _voice.StartAsync();
                Changed?.Invoke();
            });
            _outcome.TrySetResult(JoinOutcome.Admitted);
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
