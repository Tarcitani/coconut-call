using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core.Tests;

public class MembershipTests
{
    [Fact]
    public void Signaling_loss_marks_reconnecting_and_resume_restores_active()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();

        var lost = h.Run(o => h.Room.SignalingLost(a, o));
        Assert.Equal(ParticipantState.Reconnecting, h.Room.StateOf(a));
        Assert.Single(lost.To<ParticipantReconnecting>(h.Creator));

        var resumed = h.Run(o => h.Room.Resume(a, o));
        Assert.Equal(CommandResult.Ok, resumed.Result);
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(a));
        Assert.Single(resumed.To<ParticipantResumed>(h.Creator));
        Assert.Single(resumed.To<RoomSnapshot>(a));
    }

    [Fact]
    public void Reconnect_grace_expiry_removes_participant_as_timed_out()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        h.Run(o => h.Room.SignalingLost(a, o));

        Assert.Empty(h.Tick(h.Options.ReconnectGrace - TimeSpan.FromMilliseconds(1)).To<PeerLeft>(h.Creator));
        var step = h.Tick(TimeSpan.FromMilliseconds(1));

        Assert.Equal(LeaveReason.TimedOut, Assert.Single(step.To<PeerLeft>(h.Creator)).Reason);
        Assert.Null(h.Room.StateOf(a));
        Assert.Equal(CommandResult.NotAMember, h.Run(o => h.Room.Resume(a, o)).Result);
    }

    [Fact]
    public void Voluntary_leave_is_immediate_and_distinct_from_disconnection()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();

        var step = h.Run(o => h.Room.Leave(a, o));

        Assert.Equal(LeaveReason.Left, Assert.Single(step.To<PeerLeft>(h.Creator)).Reason);
        Assert.Null(h.Room.StateOf(a));
    }

    [Fact]
    public void Admin_succession_picks_longest_present_by_admission_order()
    {
        var h = new RoomHarness();
        var first = h.Join("first-joined");
        var second = h.Join("second-joined");

        // O segundo a entrar é admitido antes: ele está presente (na malha) há mais tempo.
        h.Link(second, h.Creator);
        h.Report(second, first, LinkStatus.Verified);
        h.Report(first, second, LinkStatus.Verified);
        Assert.Equal(ParticipantState.Pending, h.Room.StateOf(first));
        h.Link(first, h.Creator);
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(first));

        var step = h.Run(o => h.Room.Leave(h.Creator, o));

        Assert.Equal(second, h.Room.Admin);
        Assert.Single(step.To<AdminChanged>(first), m => m.Admin == second);
    }

    [Fact]
    public void Succession_prefers_active_over_reconnecting()
    {
        var h = new RoomHarness();
        var older = h.JoinAndAdmit("older");
        var newer = h.JoinAndAdmit("newer");
        h.Run(o => h.Room.SignalingLost(older, o));

        h.Run(o => h.Room.Leave(h.Creator, o));

        Assert.Equal(newer, h.Room.Admin);
    }

    [Fact]
    public void Reconnecting_admin_keeps_role_during_grace()
    {
        var h = new RoomHarness();
        h.JoinAndAdmit();
        h.Run(o => h.Room.SignalingLost(h.Creator, o));

        Assert.Equal(h.Creator, h.Room.Admin);
        h.Run(o => h.Room.Resume(h.Creator, o));
        Assert.Equal(h.Creator, h.Room.Admin);
    }

    [Fact]
    public void Seniority_survives_reconnection()
    {
        var h = new RoomHarness();
        var older = h.JoinAndAdmit("older");
        h.JoinAndAdmit("newer");
        h.Run(o => h.Room.SignalingLost(older, o));
        h.Run(o => h.Room.Resume(older, o));

        h.Run(o => h.Room.Leave(h.Creator, o));

        Assert.Equal(older, h.Room.Admin);
    }

    [Fact]
    public void Only_admin_can_kick_and_kicked_identity_is_revoked()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        var b = h.JoinAndAdmit();

        Assert.Equal(CommandResult.NotAllowed, h.Run(o => h.Room.Kick(a, b, o)).Result);
        Assert.Equal(CommandResult.InvalidTarget, h.Run(o => h.Room.Kick(h.Creator, h.Creator, o)).Result);

        var step = h.Run(o => h.Room.Kick(h.Creator, b, o));

        Assert.Equal(CommandResult.Ok, step.Result);
        Assert.Single(step.To<Kicked>(b));
        Assert.Equal(LeaveReason.Kicked, Assert.Single(step.To<PeerLeft>(a)).Reason);
        Assert.Equal(CommandResult.Revoked, h.Run(o => h.Room.Resume(b, o)).Result);
        Assert.Equal(CommandResult.Revoked, h.Run(o => h.Room.Join(b, "b", o)).Result);
        Assert.Equal(CommandResult.Revoked, h.Relay(b, a));
    }

    [Fact]
    public void Kicking_a_pending_participant_cancels_admission()
    {
        var h = new RoomHarness();
        var b = h.Join();

        var step = h.Run(o => h.Room.Kick(h.Creator, b, o));

        Assert.Single(step.To<Kicked>(b));
        Assert.Null(h.Room.StateOf(b));
    }

    [Fact]
    public void Only_admin_can_end_room_for_everyone()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();

        Assert.Equal(CommandResult.NotAllowed, h.Run(o => h.Room.EndRoom(a, o)).Result);

        var step = h.Run(o => h.Room.EndRoom(h.Creator, o));

        Assert.True(h.Room.IsClosed);
        Assert.Equal(new HashSet<ParticipantId> { h.Creator, a }, step.RecipientsOf<RoomEnded>().ToHashSet());
        Assert.Equal(CommandResult.RoomClosed, h.Run(o => h.Room.Join(ParticipantId.New(), "x", o)).Result);
    }

    [Fact]
    public void Empty_room_closes_only_after_reconnect_grace()
    {
        var h = new RoomHarness();
        h.Run(o => h.Room.SignalingLost(h.Creator, o));

        h.Tick(h.Options.ReconnectGrace - TimeSpan.FromSeconds(1));
        Assert.False(h.Room.IsClosed);

        h.Tick(TimeSpan.FromSeconds(1));
        Assert.True(h.Room.IsClosed);
    }

    [Fact]
    public void Last_voluntary_leave_closes_room()
    {
        var h = new RoomHarness();

        h.Run(o => h.Room.Leave(h.Creator, o));

        Assert.True(h.Room.IsClosed);
    }

    [Fact]
    public void Non_member_commands_are_rejected_without_side_effects()
    {
        var h = new RoomHarness();
        var stranger = ParticipantId.New();

        var step = h.Run(o => h.Room.Kick(stranger, h.Creator, o));

        Assert.Equal(CommandResult.NotAMember, step.Result);
        Assert.Empty(step.Outbox.Items);
        Assert.Equal(CommandResult.NotAMember, h.Report(stranger, h.Creator).Result);
    }
}
