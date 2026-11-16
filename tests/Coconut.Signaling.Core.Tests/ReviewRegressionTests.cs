using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core.Tests;

/// <summary>Cenários da revisão independente de 2026-11-16 (achados 1–10), mantidos como regressão.</summary>
public class ReviewRegressionTests
{
    [Fact]
    public void Resume_after_grace_fails_even_without_tick()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        h.Run(o => h.Room.SignalingLost(a, o));
        h.Clock.Advance(h.Options.ReconnectGrace + TimeSpan.FromSeconds(15));

        var step = h.Run(o => h.Room.Resume(a, o));

        Assert.Equal(CommandResult.NotAMember, step.Result);
        Assert.Equal(LeaveReason.TimedOut, Assert.Single(step.To<PeerLeft>(h.Creator)).Reason);
    }

    [Fact]
    public void Link_report_after_admission_deadline_does_not_admit_even_without_tick()
    {
        var h = new RoomHarness();
        var b = h.Join();
        h.Report(b, h.Creator);
        h.Clock.Advance(h.Options.AdmissionTimeout * 3);

        var step = h.Report(h.Creator, b);

        Assert.Equal(AdmissionFailure.Timeout, Assert.Single(step.To<AdmissionFailed>(b)).Reason);
        Assert.Null(h.Room.StateOf(b));
    }

    [Fact]
    public void Coarse_tick_applies_deadlines_chronologically_for_admission()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit("a");
        h.Run(o => h.Room.SignalingLost(a, o));          // tolerância vence em t=30
        h.Clock.Advance(TimeSpan.FromSeconds(10.5));
        var b = h.Join("b");                              // prazo de admissão em t=30,5
        h.Link(b, h.Creator);
        Assert.Equal(ParticipantState.Pending, h.Room.StateOf(b));

        h.Tick(TimeSpan.FromSeconds(20.5));               // t=31, um único tick

        Assert.Null(h.Room.StateOf(a));
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(b));
    }

    [Fact]
    public void Coarse_tick_expires_share_request_before_later_sharer_removal()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit("a");
        var b = h.JoinAndAdmit("b");
        h.Run(o => h.Room.RequestShare(a, o));
        h.Run(o => h.Room.RequestShare(b, o));            // pedido expira em t=30
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        h.Run(o => h.Room.SignalingLost(a, o));          // transmissor sai em t=31

        var step = h.Tick(TimeSpan.FromSeconds(31));      // t=32

        Assert.Single(step.To<ShareRequestExpired>(b));
        Assert.Null(h.Room.Share.Sharer);
    }

    [Fact]
    public void Resume_only_announces_pendings_that_joined_during_absence()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit("a");
        h.Join("before");
        h.Run(o => h.Room.SignalingLost(a, o));
        var during = h.Join("during");

        var step = h.Run(o => h.Room.Resume(a, o));

        Assert.Equal([during], step.To<ConnectToPeer>(a).Select(c => c.Peer.Id));
    }

    [Fact]
    public void Resume_of_member_never_seen_disconnected_does_not_reannounce_pendings()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit("a");
        h.Join("b");

        var step = h.Run(o => h.Room.Resume(a, o));

        Assert.Empty(step.To<ConnectToPeer>(a));
    }

    [Fact]
    public void Pendings_do_not_inherit_room_when_last_admitted_member_leaves()
    {
        var h = new RoomHarness();
        var p1 = h.Join("p1");
        var p2 = h.Join("p2");
        h.Link(p1, p2);

        var step = h.Run(o => h.Room.Leave(h.Creator, o));

        Assert.True(h.Room.IsClosed);
        Assert.Null(h.Room.Admin);
        Assert.Equal(AdmissionFailure.RoomEmptied, Assert.Single(step.To<AdmissionFailed>(p1)).Reason);
        Assert.Equal(AdmissionFailure.RoomEmptied, Assert.Single(step.To<AdmissionFailed>(p2)).Reason);
    }

    [Fact]
    public void Pending_does_not_inherit_room_when_reconnecting_admin_expires()
    {
        var h = new RoomHarness();
        var p = h.Join();
        h.Run(o => h.Room.SignalingLost(h.Creator, o));

        h.Tick(h.Options.ReconnectGrace);

        Assert.True(h.Room.IsClosed);
        Assert.Null(h.Room.StateOf(p));
    }

    [Fact]
    public void Sharer_resuming_receives_pending_request_again()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit("a");
        var b = h.JoinAndAdmit("b");
        h.Run(o => h.Room.RequestShare(a, o));
        h.Run(o => h.Room.SignalingLost(a, o));
        h.Run(o => h.Room.RequestShare(b, o));

        var step = h.Run(o => h.Room.Resume(a, o));

        Assert.Equal(b, Assert.Single(step.To<ShareRequested>(a)).Requester);
    }

    [Fact]
    public void Sharer_resuming_during_handover_is_told_to_stop_again()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit("a");
        var b = h.JoinAndAdmit("b");
        h.Run(o => h.Room.RequestShare(a, o));
        var request = Assert.Single(h.Run(o => h.Room.RequestShare(b, o)).To<ShareRequested>(a)).Request;
        h.Run(o => h.Room.RespondToShareRequest(a, request, true, o));
        h.Run(o => h.Room.SignalingLost(a, o));

        var step = h.Run(o => h.Room.Resume(a, o));

        Assert.Single(step.To<StopSharing>(a));
    }

    [Fact]
    public void Kicking_pending_tells_others_the_reason()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        var b = h.Join();

        var step = h.Run(o => h.Room.Kick(h.Creator, b, o));

        Assert.Equal(LeaveReason.Kicked, Assert.Single(step.To<DropPeer>(a)).Reason);
    }

    [Fact]
    public void Cancel_after_accept_lets_sharer_finish_and_room_becomes_idle()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit("a");
        var b = h.JoinAndAdmit("b");
        h.Run(o => h.Room.RequestShare(a, o));
        var session = h.Room.Share.Session!.Value;
        var request = Assert.Single(h.Run(o => h.Room.RequestShare(b, o)).To<ShareRequested>(a)).Request;
        h.Run(o => h.Room.RespondToShareRequest(a, request, true, o));

        h.Run(o => h.Room.CancelShareRequest(b, request, o));
        h.Run(o => h.Room.ShareStopped(a, session, o));

        Assert.Null(h.Room.Share.Sharer);
    }

    [Fact]
    public void Timeout_identifies_blocking_peer_as_reconnecting()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        h.Run(o => h.Room.SignalingLost(a, o));
        var b = h.Join();
        h.Link(b, h.Creator);

        var step = h.Tick(h.Options.AdmissionTimeout);

        var peer = Assert.Single(Assert.Single(step.To<AdmissionFailed>(b)).FailedPeers);
        Assert.Equal(a, peer.Id);
        Assert.Equal(ParticipantState.Reconnecting, peer.State);
    }

    [Fact]
    public void Pending_does_not_receive_call_state_broadcasts()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        var b = h.Join();

        var share = h.Run(o => h.Room.RequestShare(a, o));
        var lost = h.Run(o => h.Room.SignalingLost(a, o));

        Assert.Empty(share.To<ShareStateChanged>(b));
        Assert.Empty(lost.To<ParticipantReconnecting>(b));
    }

    [Fact]
    public void Identity_cannot_rejoin_after_leaving()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        h.Run(o => h.Room.Leave(a, o));

        Assert.Equal(CommandResult.IdentityReused, h.Run(o => h.Room.Join(a, "a", o)).Result);
    }

    [Fact]
    public void Succession_falls_back_to_oldest_reconnecting_when_no_one_is_active()
    {
        var h = new RoomHarness();
        var older = h.JoinAndAdmit("older");
        var newer = h.JoinAndAdmit("newer");
        h.Run(o => h.Room.SignalingLost(newer, o));
        h.Run(o => h.Room.SignalingLost(older, o));

        h.Run(o => h.Room.Leave(h.Creator, o));

        Assert.Equal(older, h.Room.Admin);
    }
}
