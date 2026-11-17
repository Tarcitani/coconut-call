using Coconut.Client.Screen;

namespace Coconut.Client.Tests;

public sealed class ScreenRateTests
{
    [Fact]
    public void Worst_receiver_reduces_budget_then_clean_reports_recover_without_exceeding_limit()
    {
        var time = new Clock(); var rate = new ScreenRateController(2_000_000, time);
        rate.Report(Guid.NewGuid(), 0); var poor = Guid.NewGuid(); rate.Report(poor, 64);
        Assert.Equal(1_200_000, rate.Tick()); Assert.Equal(1_200_000, rate.Tick());
        time.Advance(5); rate.Report(poor, 0); Assert.Equal(1_320_000, rate.Tick());
        for (var i = 0; i < 30; i++) { time.Advance(5); rate.Report(poor, 0); rate.Tick(); }
        Assert.Equal(2_000_000, rate.Bitrate);
    }

    [Fact]
    public void Expired_reports_and_unknown_receivers_do_not_throttle_forever()
    {
        var time = new Clock(); var rate = new ScreenRateController(2_000_000, time);
        rate.Report(Guid.NewGuid(), 64); Assert.Equal(1_200_000, rate.Tick());
        time.Advance(10); Assert.Equal(1_200_000, rate.Tick());
        rate.Report(Guid.NewGuid(), 0); Assert.Equal(1_320_000, rate.Tick());
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now += TimeSpan.FromSeconds(seconds);
    }

    [Fact]
    public void Sustained_severe_video_loss_stops_video_instead_of_competing_with_voice_indefinitely()
    {
        var time = new Clock(); var rate = new ScreenRateController(2_000_000, time); var peer = Guid.NewGuid();
        rate.Report(peer, 180); rate.Tick(); Assert.False(rate.ShouldStopForCongestion);
        time.Advance(2); rate.Report(peer, 180); rate.Tick(); Assert.True(rate.ShouldStopForCongestion);
    }

    [Fact]
    public void Round_trip_growth_reduces_budget_without_penalizing_a_stable_distant_peer()
    {
        var time = new Clock(); var rate = new ScreenRateController(2_000_000, time); var peer = Guid.NewGuid();
        rate.Report(peer, 0); rate.ReportRoundTrip(peer, TimeSpan.FromMilliseconds(500)); Assert.Equal(2_000_000, rate.Tick());
        time.Advance(2); rate.ReportRoundTrip(peer, TimeSpan.FromMilliseconds(900)); Assert.Equal(1_600_000, rate.Tick());
    }
}
