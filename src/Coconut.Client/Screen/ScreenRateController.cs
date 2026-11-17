namespace Coconut.Client.Screen;

/// <summary>Worst current receiver wins; expired reports cannot permanently throttle a call.</summary>
public sealed class ScreenRateController(int maximumBitrate, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly int _maximumBitrate = maximumBitrate;
    private readonly Dictionary<Guid, (byte Loss, DateTimeOffset At)> _reports = [];
    private DateTimeOffset _lastChange;
    private readonly Dictionary<Guid, (TimeSpan Baseline, TimeSpan Current, DateTimeOffset At)> _latency = [];
    private int _severeIntervals;

    public int Bitrate { get; private set; } = maximumBitrate;
    public bool ShouldStopForCongestion { get; private set; }
    public void Remove(Guid peer) { _reports.Remove(peer); _latency.Remove(peer); }

    public void Report(Guid peer, byte fractionLost) => _reports[peer] = (fractionLost, _clock.GetUtcNow());
    public void ReportRoundTrip(Guid peer, TimeSpan roundTrip)
    {
        var baseline = _latency.TryGetValue(peer, out var previous) && previous.Baseline < roundTrip ? previous.Baseline : roundTrip;
        _latency[peer] = (baseline, roundTrip, _clock.GetUtcNow());
    }

    public int Tick()
    {
        var now = _clock.GetUtcNow();
        foreach (var key in _reports.Where(p => now - p.Value.At > TimeSpan.FromSeconds(6)).Select(p => p.Key).ToArray()) { _reports.Remove(key); }
        foreach (var key in _latency.Where(p => now - p.Value.At > TimeSpan.FromSeconds(6)).Select(p => p.Key).ToArray()) { _latency.Remove(key); }
        if (now - _lastChange < TimeSpan.FromSeconds(2) || _reports.Count == 0) { return Bitrate; }
        var worst = _reports.Values.Max(p => p.Loss);
        _severeIntervals = worst >= 128 ? _severeIntervals + 1 : 0;
        if (_severeIntervals >= 2) { ShouldStopForCongestion = true; }
        if (_latency.Values.Any(p => p.Current - p.Baseline > TimeSpan.FromMilliseconds(200) && p.Current.TotalMilliseconds > p.Baseline.TotalMilliseconds * 1.5)) { worst = Math.Max(worst, (byte)13); }
        var next = worst > 12 ? Math.Max(256_000, Bitrate * (worst > 51 ? 60 : 80) / 100)
            : worst < 3 && now - _lastChange >= TimeSpan.FromSeconds(5) ? Math.Min(_maximumBitrate, Bitrate + Math.Max(64_000, Bitrate / 10)) : Bitrate;
        if (next != Bitrate) { Bitrate = next; _lastChange = now; }
        return Bitrate;
    }
}
