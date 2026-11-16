using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core.Tests;

public class AdmissionTests
{
    [Fact]
    public void Creator_is_active_admin_of_new_room()
    {
        var h = new RoomHarness();

        Assert.Equal(ParticipantState.Active, h.Room.StateOf(h.Creator));
        Assert.Equal(h.Creator, h.Room.Admin);
        Assert.Equal(1, h.Room.Epoch);
        Assert.Single(h.Created.To<RoomSnapshot>(h.Creator));
    }

    [Fact]
    public void Joiner_starts_pending_and_is_offerer_to_everyone()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit("a");

        var b = ParticipantId.New();
        var step = h.Run(o => h.Room.Join(b, "b", o));

        Assert.Equal(ParticipantState.Pending, h.Room.StateOf(b));
        var started = Assert.Single(step.To<AdmissionStarted>(b));
        Assert.Equal(new HashSet<ParticipantId> { h.Creator, a }, started.PeersToConnect.Select(p => p.Id).ToHashSet());
        Assert.Equal(NegotiationRole.Answerer, Assert.Single(step.To<ConnectToPeer>(h.Creator)).Role);
        Assert.Equal(NegotiationRole.Answerer, Assert.Single(step.To<ConnectToPeer>(a)).Role);
    }

    [Fact]
    public void Admission_requires_both_endpoints_to_verify_the_link()
    {
        var h = new RoomHarness();
        var b = h.Join();

        h.Report(b, h.Creator);
        Assert.Equal(ParticipantState.Pending, h.Room.StateOf(b));

        var step = h.Report(h.Creator, b);
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(b));
        Assert.Equal(2, h.Room.Epoch);
        Assert.Equal(new HashSet<ParticipantId> { h.Creator, b }, step.RecipientsOf<PeerAdmitted>().ToHashSet());
    }

    [Fact]
    public void Admission_requires_links_with_every_active_participant()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        var b = h.Join();

        h.Link(b, h.Creator);
        Assert.Equal(ParticipantState.Pending, h.Room.StateOf(b));

        h.Link(b, a);
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(b));
    }

    [Fact]
    public void Failed_link_rolls_back_admission_and_preserves_established_call()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        var epoch = h.Room.Epoch;
        var b = h.Join();
        h.Link(b, h.Creator);

        var step = h.Report(a, b, LinkStatus.Failed);

        Assert.Null(h.Room.StateOf(b));
        var failed = Assert.Single(step.To<AdmissionFailed>(b));
        Assert.Equal(AdmissionFailure.PeerLinkFailed, failed.Reason);
        Assert.Equal([a], failed.FailedPeers.Select(p => p.Id));
        Assert.Single(step.To<DropPeer>(h.Creator));
        Assert.Single(step.To<DropPeer>(a));
        Assert.Empty(step.To<PeerLeft>(a));
        Assert.Equal(epoch, h.Room.Epoch);
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(a));
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(h.Creator));
    }

    [Fact]
    public void Admission_times_out_listing_unverified_peers()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        var b = h.Join();
        h.Link(b, h.Creator);

        var early = h.Tick(h.Options.AdmissionTimeout - TimeSpan.FromMilliseconds(1));
        Assert.Empty(early.To<AdmissionFailed>(b));

        var step = h.Tick(TimeSpan.FromMilliseconds(1));
        var failed = Assert.Single(step.To<AdmissionFailed>(b));
        Assert.Equal(AdmissionFailure.Timeout, failed.Reason);
        Assert.Equal([a], failed.FailedPeers.Select(p => p.Id));
        Assert.Null(h.Room.StateOf(b));
    }

    [Fact]
    public void Concurrent_joiners_must_also_link_with_each_other_once_one_is_admitted()
    {
        var h = new RoomHarness();
        var a = h.Join("a");
        var b = h.Join("b");

        h.Link(a, h.Creator);
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(a));

        h.Link(b, h.Creator);
        Assert.Equal(ParticipantState.Pending, h.Room.StateOf(b));

        h.Link(b, a);
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(b));
    }

    [Fact]
    public void Later_joiner_is_not_queued_behind_an_earlier_pending()
    {
        var h = new RoomHarness();
        var a = h.Join("a");
        var b = h.Join("b");

        h.Link(b, h.Creator);

        Assert.Equal(ParticipantState.Active, h.Room.StateOf(b));
        Assert.Equal(ParticipantState.Pending, h.Room.StateOf(a));
    }

    [Fact]
    public void Link_between_pendings_established_early_makes_second_admission_immediate()
    {
        var h = new RoomHarness();
        var a = h.Join("a");
        var b = h.Join("b");
        h.Link(b, a);
        h.Link(b, h.Creator);

        var step = h.Link(a, h.Creator);

        Assert.Equal(ParticipantState.Active, h.Room.StateOf(a));
        Assert.Equal(a, Assert.Single(step.To<PeerAdmitted>(h.Creator)).Admitted);
    }

    [Fact]
    public void Failed_link_between_two_pendings_only_matters_once_one_is_admitted()
    {
        var h = new RoomHarness();
        var a = h.Join("a");
        var b = h.Join("b");

        var report = h.Report(b, a, LinkStatus.Failed);
        Assert.Empty(report.To<AdmissionFailed>(b));
        Assert.Equal(ParticipantState.Pending, h.Room.StateOf(b));

        var step = h.Link(a, h.Creator);

        Assert.Equal(ParticipantState.Active, h.Room.StateOf(a));
        Assert.Equal([a], Assert.Single(step.To<AdmissionFailed>(b)).FailedPeers.Select(p => p.Id));
        Assert.Null(h.Room.StateOf(b));
    }

    [Fact]
    public void Active_participant_leaving_during_admission_unblocks_the_joiner()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        var b = h.Join();
        h.Link(b, h.Creator);

        h.Run(o => h.Room.Leave(a, o));

        Assert.Equal(ParticipantState.Active, h.Room.StateOf(b));
    }

    [Fact]
    public void Pending_losing_signaling_cancels_admission()
    {
        var h = new RoomHarness();
        var b = h.Join();

        var step = h.Run(o => h.Room.SignalingLost(b, o));

        Assert.Null(h.Room.StateOf(b));
        Assert.Single(step.To<DropPeer>(h.Creator));
    }

    [Fact]
    public void Reconnecting_participant_is_still_required_for_admission()
    {
        var h = new RoomHarness();
        var a = h.JoinAndAdmit();
        h.Run(o => h.Room.SignalingLost(a, o));
        var b = h.Join();

        h.Link(b, h.Creator);
        Assert.Equal(ParticipantState.Pending, h.Room.StateOf(b));

        var resumed = h.Run(o => h.Room.Resume(a, o));
        Assert.Single(resumed.To<ConnectToPeer>(a), c => c.Peer.Id == b);

        h.Link(b, a);
        Assert.Equal(ParticipantState.Active, h.Room.StateOf(b));
    }

    [Fact]
    public void Capacity_counts_pending_participants()
    {
        var h = new RoomHarness(new RoomOptions { Capacity = 3 });
        h.JoinAndAdmit();
        h.Join();

        var extra = ParticipantId.New();
        var step = h.Run(o => h.Room.Join(extra, "x", o));

        Assert.Equal(CommandResult.RoomFull, step.Result);
        Assert.Single(step.To<RoomFull>(extra));
        Assert.Null(h.Room.StateOf(extra));
    }

    [Fact]
    public void Full_mesh_of_fifteen_is_admitted()
    {
        var h = new RoomHarness();
        for (var i = 0; i < 14; i++)
        {
            h.JoinAndAdmit($"p{i}");
        }

        Assert.Equal(15, h.Room.MemberCount);
        Assert.Equal(15, h.Mesh.Count());
        Assert.Equal(CommandResult.RoomFull, h.Run(o => h.Room.Join(ParticipantId.New(), "x", o)).Result);
    }

    [Fact]
    public void Relay_is_only_allowed_between_members()
    {
        var h = new RoomHarness();
        var b = h.Join();
        var stranger = ParticipantId.New();

        Assert.Equal(CommandResult.Ok, h.Relay(b, h.Creator));
        Assert.Equal(CommandResult.InvalidTarget, h.Relay(b, stranger));
        Assert.Equal(CommandResult.InvalidTarget, h.Relay(b, b));
        Assert.Equal(CommandResult.NotAMember, h.Relay(stranger, h.Creator));
    }

    [Fact]
    public void Duplicate_join_is_rejected()
    {
        var h = new RoomHarness();
        var b = h.Join();

        Assert.Equal(CommandResult.AlreadyMember, h.Run(o => h.Room.Join(b, "b", o)).Result);
    }
}
