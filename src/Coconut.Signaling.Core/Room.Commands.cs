using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core;

/// <summary>Comandos administrativos e de compartilhamento, com autorização no servidor.</summary>
public sealed partial class Room
{
    public CommandResult Kick(ParticipantId actor, ParticipantId target, Outbox outbox)
    {
        var guard = GuardAdmin(actor, outbox);
        if (guard != CommandResult.Ok)
        {
            return guard;
        }

        if (target == actor || !_members.ContainsKey(target))
        {
            return CommandResult.InvalidTarget;
        }

        outbox.Send(target, new Kicked());
        _revoked.Add(target);
        Remove(target, LeaveReason.Kicked, outbox);
        EvaluateAdmissions(outbox);
        return CommandResult.Ok;
    }

    public CommandResult EndRoom(ParticipantId actor, Outbox outbox)
    {
        var guard = GuardAdmin(actor, outbox);
        if (guard != CommandResult.Ok)
        {
            return guard;
        }

        foreach (var id in _members.Keys)
        {
            outbox.Send(id, new RoomEnded());
        }

        _revoked.UnionWith(_members.Keys);
        _members.Clear();
        Share.Reset();
        Admin = null;
        IsClosed = true;
        return CommandResult.Ok;
    }

    public CommandResult RequestShare(ParticipantId actor, Outbox outbox)
    {
        var guard = Guard(actor, requireActive: true, outbox);
        return guard != CommandResult.Ok ? guard : Share.Request(actor, outbox);
    }

    public CommandResult RespondToShareRequest(ParticipantId actor, ShareRequestId request, bool accept, Outbox outbox)
    {
        var guard = Guard(actor, requireActive: true, outbox);
        return guard != CommandResult.Ok ? guard : Share.Respond(actor, request, accept, outbox);
    }

    public CommandResult CancelShareRequest(ParticipantId actor, ShareRequestId request, Outbox outbox)
    {
        var guard = Guard(actor, requireActive: false, outbox);
        return guard != CommandResult.Ok ? guard : Share.Cancel(actor, request, outbox);
    }

    public CommandResult ShareStopped(ParticipantId actor, ShareSessionId session, Outbox outbox)
    {
        var guard = Guard(actor, requireActive: false, outbox);
        return guard != CommandResult.Ok ? guard : Share.Stopped(actor, session, outbox);
    }

    private CommandResult GuardAdmin(ParticipantId actor, Outbox outbox)
    {
        var guard = Guard(actor, requireActive: true, outbox);
        if (guard != CommandResult.Ok)
        {
            return guard;
        }

        return Admin == actor ? CommandResult.Ok : CommandResult.NotAllowed;
    }
}
