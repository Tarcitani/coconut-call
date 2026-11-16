using Coconut.App.Core.Services;
using Coconut.Audio;
using Coconut.Signaling.Contracts;

namespace Coconut.App.Core.Tests;

internal sealed class FakeBackend : ICallBackend
{
    public JoinOutcome Outcome { get; set; } = JoinOutcome.Admitted;

    public List<string> Calls { get; } = [];

    public bool Disposed { get; private set; }

    public event Action? Changed;

    public event Action<string>? Problem;

    public event Action<string>? Ended;

    public string? InviteCode { get; set; } = "ABCDE12345";

    public ParticipantId? You { get; set; } = ParticipantId.New();

    public ParticipantId? Admin { get; set; }

    public IReadOnlyList<PeerInfo> Roster { get; set; } = [];

    public bool IsMuted { get; set; }

    public double MicrophoneLevel { get; set; }

    public IReadOnlyDictionary<ParticipantId, double> SpeakingLevels { get; set; } = new Dictionary<ParticipantId, double>();

    public List<AudioDeviceInfo> CaptureDevices { get; } =
    [
        new("mic-1", "Microfone USB", AudioDirection.Capture, true),
        new("mic-2", "Headset", AudioDirection.Capture, false),
    ];

    public Dictionary<AudioDirection, string?> Selected { get; } = new() { [AudioDirection.Capture] = null, [AudioDirection.Render] = null };

    public Task<JoinOutcome> CreateRoomAsync(Uri server, string displayName, CancellationToken cancellation)
    {
        Calls.Add($"create:{server}:{displayName}");
        return Task.FromResult(Outcome);
    }

    public Task<JoinOutcome> JoinRoomAsync(Uri server, string inviteCode, string displayName, CancellationToken cancellation)
    {
        Calls.Add($"join:{inviteCode}:{displayName}");
        return Task.FromResult(Outcome);
    }

    public Task LeaveAsync()
    {
        Calls.Add("leave");
        return Task.CompletedTask;
    }

    public Task KickAsync(ParticipantId target)
    {
        Calls.Add($"kick:{target}");
        return Task.CompletedTask;
    }

    public Task EndRoomForAllAsync()
    {
        Calls.Add("end");
        return Task.CompletedTask;
    }

    public IReadOnlyList<AudioDeviceInfo> Devices(AudioDirection direction) =>
        direction == AudioDirection.Capture ? CaptureDevices : [];

    public string? SelectedDevice(AudioDirection direction) => Selected[direction];

    public Task SelectDeviceAsync(AudioDirection direction, string? deviceId)
    {
        Selected[direction] = deviceId;
        Calls.Add($"device:{direction}:{deviceId}");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public void RaiseChanged() => Changed?.Invoke();

    public void RaiseProblem(string message) => Problem?.Invoke(message);

    public void RaiseEnded(string reason) => Ended?.Invoke(reason);
}

/// <summary>Executa na hora; o timer avança manualmente com <see cref="Tick"/>.</summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    private readonly List<Action> _timers = [];

    public void Post(Action action) => action();

    public IDisposable StartTimer(TimeSpan interval, Action tick)
    {
        _timers.Add(tick);
        return new Stop(() => _timers.Remove(tick));
    }

    public void Tick()
    {
        foreach (var timer in _timers.ToList())
        {
            timer();
        }
    }

    public int ActiveTimers => _timers.Count;

    private sealed class Stop(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}

internal sealed class FakeClipboard : IClipboard
{
    public string? Text { get; private set; }

    public void SetText(string text) => Text = text;
}

internal sealed class MemorySettings : ISettingsStore
{
    public AppSettings Current { get; set; } = new();

    public AppSettings Load() => Current;

    public void Save(AppSettings settings) => Current = settings;
}
