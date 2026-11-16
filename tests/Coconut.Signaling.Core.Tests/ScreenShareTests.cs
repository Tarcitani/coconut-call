using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core.Tests;

public class ScreenShareTests
{
    private static (RoomHarness H, ParticipantId A, ParticipantId B) RoomWithSharer()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit("a");
        var b = h.JoinAndAdmit("b");
        Assert.Equal(CommandResult.Ok, h.Run(o => h.Room.RequestShare(a, o)).Result);
        return (h, a, b);
    }

    [Fact]
    public void Request_in_idle_room_is_granted_and_announced_to_all()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();

        var step = h.Run(o => h.Room.RequestShare(a, o));

        Assert.Equal(a, h.Room.Share.Sharer);
        Assert.NotNull(h.Room.Share.Session);
        Assert.Equal(new HashSet<ParticipantId> { h.Creator, a }, step.RecipientsOf<ShareStateChanged>().ToHashSet());
    }

    [Fact]
    public void Request_while_sharing_asks_current_sharer()
    {
        var (h, a, b) = RoomWithSharer();

        var step = h.Run(o => h.Room.RequestShare(b, o));

        var asked = Assert.Single(step.To<ShareRequested>(a));
        Assert.Equal(b, asked.Requester);
        Assert.Single(step.To<ShareRequestPending>(b));
        Assert.Equal(a, h.Room.Share.Sharer);
    }

    [Fact]
    public void Accepted_handover_stops_old_session_before_granting_new_one()
    {
        var (h, a, b) = RoomWithSharer();
        var oldSession = h.Room.Share.Session!.Value;
        var request = Assert.Single(h.Run(o => h.Room.RequestShare(b, o)).To<ShareRequested>(a)).Request;

        var accepted = h.Run(o => h.Room.RespondToShareRequest(a, request, accept: true, o));
        Assert.Equal(oldSession, Assert.Single(accepted.To<StopSharing>(a)).Session);
        Assert.Equal(a, h.Room.Share.Sharer);

        var stopped = h.Run(o => h.Room.ShareStopped(a, oldSession, o));

        Assert.Equal(b, h.Room.Share.Sharer);
        Assert.NotEqual(oldSession, h.Room.Share.Session);
        Assert.All(stopped.Outbox.Items.Select(i => i.Message).OfType<ShareStateChanged>(), m => Assert.Equal(b, m.Sharer));
    }

    [Fact]
    public void Declined_request_keeps_current_sharer()
    {
        var (h, a, b) = RoomWithSharer();
        var request = Assert.Single(h.Run(o => h.Room.RequestShare(b, o)).To<ShareRequested>(a)).Request;

        var step = h.Run(o => h.Room.RespondToShareRequest(a, request, accept: false, o));

        Assert.Single(step.To<ShareRequestDeclined>(b));
        Assert.Equal(a, h.Room.Share.Sharer);
    }

    [Fact]
    public void Concurrent_request_is_rejected_as_busy()
    {
        var (h, a, b) = RoomWithSharer();
        h.Run(o => h.Room.RequestShare(b, o));

        var step = h.Run(o => h.Room.RequestShare(h.Creator, o));

        Assert.Equal(CommandResult.Busy, step.Result);
        Assert.Single(step.To<ShareBusy>(h.Creator));
        Assert.Empty(step.To<ShareRequested>(a));
    }

    [Fact]
    public void Only_current_sharer_can_answer_a_request()
    {
        var (h, a, b) = RoomWithSharer();
        var request = Assert.Single(h.Run(o => h.Room.RequestShare(b, o)).To<ShareRequested>(a)).Request;

        Assert.Equal(CommandResult.NotAllowed, h.Run(o => h.Room.RespondToShareRequest(h.Creator, request, true, o)).Result);
        Assert.Equal(CommandResult.UnknownRequest, h.Run(o => h.Room.RespondToShareRequest(a, ShareRequestId.New(), true, o)).Result);
    }

    [Fact]
    public void Unanswered_request_expires()
    {
        var (h, a, b) = RoomWithSharer();
        h.Run(o => h.Room.RequestShare(b, o));

        var step = h.Tick(h.Options.ShareRequestTimeout);

        Assert.Single(step.To<ShareRequestExpired>(b));
        Assert.Single(step.To<ShareRequestExpired>(a));
        Assert.Equal(CommandResult.Ok, h.Run(o => h.Room.RequestShare(h.Creator, o)).Result);
    }

    [Fact]
    public void Handover_is_forced_when_old_sharer_does_not_confirm()
    {
        var (h, a, b) = RoomWithSharer();
        var request = Assert.Single(h.Run(o => h.Room.RequestShare(b, o)).To<ShareRequested>(a)).Request;
        h.Run(o => h.Room.RespondToShareRequest(a, request, true, o));

        h.Tick(h.Options.HandoverTimeout);

        Assert.Equal(b, h.Room.Share.Sharer);
    }

    [Fact]
    public void Requester_can_cancel_and_sharer_is_notified()
    {
        var (h, a, b) = RoomWithSharer();
        var request = Assert.Single(h.Run(o => h.Room.RequestShare(b, o)).To<ShareRequested>(a)).Request;

        Assert.Equal(CommandResult.UnknownRequest, h.Run(o => h.Room.CancelShareRequest(h.Creator, request, o)).Result);
        var step = h.Run(o => h.Room.CancelShareRequest(b, request, o));

        Assert.Single(step.To<ShareRequestCanceled>(a));
        Assert.Equal(CommandResult.Ok, h.Run(o => h.Room.RequestShare(h.Creator, o)).Result);
    }

    [Fact]
    public void Sharer_leaving_with_pending_request_grants_requester()
    {
        var (h, a, b) = RoomWithSharer();
        h.Run(o => h.Room.RequestShare(b, o));

        h.Run(o => h.Room.Leave(a, o));

        Assert.Equal(b, h.Room.Share.Sharer);
    }

    [Fact]
    public void Requester_leaving_cancels_request()
    {
        var (h, a, b) = RoomWithSharer();
        h.Run(o => h.Room.RequestShare(b, o));

        var step = h.Run(o => h.Room.Leave(b, o));

        Assert.Single(step.To<ShareRequestCanceled>(a));
        Assert.Equal(a, h.Room.Share.Sharer);
    }

    [Fact]
    public void Sharer_stopping_voluntarily_returns_room_to_idle()
    {
        var (h, a, _) = RoomWithSharer();

        var step = h.Run(o => h.Room.ShareStopped(a, h.Room.Share.Session!.Value, o));

        Assert.Null(h.Room.Share.Sharer);
        Assert.All(step.Outbox.Items.Select(i => i.Message).OfType<ShareStateChanged>(), m => Assert.Null(m.Sharer));
    }

    [Fact]
    public void Stale_session_stop_is_ignored()
    {
        var (h, a, _) = RoomWithSharer();

        Assert.Equal(CommandResult.UnknownRequest, h.Run(o => h.Room.ShareStopped(a, ShareSessionId.New(), o)).Result);
        Assert.Equal(a, h.Room.Share.Sharer);
    }

    [Fact]
    public void Pending_participant_cannot_share()
    {
        var h = new RoomHarness();
        var b = h.Join();

        Assert.Equal(CommandResult.NotAllowed, h.Run(o => h.Room.RequestShare(b, o)).Result);
        Assert.Null(h.Room.Share.Sharer);
    }

    [Fact]
    public void Sharer_cannot_request_again()
    {
        var (h, a, _) = RoomWithSharer();

        Assert.Equal(CommandResult.AlreadySharing, h.Run(o => h.Room.RequestShare(a, o)).Result);
    }
}
