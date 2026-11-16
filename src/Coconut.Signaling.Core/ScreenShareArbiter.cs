using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core;

/// <summary>
/// Garante no máximo uma transmissão de tela por sala e coordena a troca de transmissor
/// (pedido → aceite/recusa → encerramento → concessão). Ver docs/protocol/salas-e-admissao.md §6.
/// </summary>
public sealed class ScreenShareArbiter
{
    private readonly RoomOptions _options;
    private readonly TimeProvider _clock;
    private readonly Func<IEnumerable<ParticipantId>> _recipients;

    private PendingRequest? _request;
    private DateTimeOffset? _handoverDeadline;

    internal ScreenShareArbiter(RoomOptions options, TimeProvider clock, Func<IEnumerable<ParticipantId>> recipients)
    {
        _options = options;
        _clock = clock;
        _recipients = recipients;
    }

    public ParticipantId? Sharer { get; private set; }

    public ShareSessionId? Session { get; private set; }

    public bool HandoverInProgress => _handoverDeadline is not null;

    /// <summary>Próximo prazo do compartilhamento (handover ou expiração do pedido), aplicado pela sala em ordem cronológica.</summary>
    internal DateTimeOffset? NextDeadline => _handoverDeadline ?? _request?.ExpiresAt;

    internal CommandResult Request(ParticipantId requester, Outbox outbox)
    {
        if (Sharer is null)
        {
            Grant(requester, outbox);
            return CommandResult.Ok;
        }

        if (Sharer == requester)
        {
            return CommandResult.AlreadySharing;
        }

        if (_request is not null || HandoverInProgress)
        {
            outbox.Send(requester, new ShareBusy());
            return CommandResult.Busy;
        }

        var expiresAt = _clock.GetUtcNow() + _options.ShareRequestTimeout;
        _request = new PendingRequest(ShareRequestId.New(), requester, expiresAt);
        outbox.Send(Sharer.Value, new ShareRequested(_request.Id, requester, expiresAt));
        outbox.Send(requester, new ShareRequestPending(_request.Id, expiresAt));
        return CommandResult.Ok;
    }

    internal CommandResult Respond(ParticipantId actor, ShareRequestId requestId, bool accept, Outbox outbox)
    {
        if (_request is null || _request.Id != requestId || HandoverInProgress)
        {
            return CommandResult.UnknownRequest;
        }

        if (actor != Sharer)
        {
            return CommandResult.NotAllowed;
        }

        if (!accept)
        {
            outbox.Send(_request.Requester, new ShareRequestDeclined(_request.Id));
            _request = null;
            return CommandResult.Ok;
        }

        _handoverDeadline = _clock.GetUtcNow() + _options.HandoverTimeout;
        outbox.Send(actor, new StopSharing(Session!.Value));
        return CommandResult.Ok;
    }

    internal CommandResult Cancel(ParticipantId actor, ShareRequestId requestId, Outbox outbox)
    {
        if (_request is null || _request.Id != requestId || _request.Requester != actor)
        {
            return CommandResult.UnknownRequest;
        }

        if (Sharer is { } sharer)
        {
            outbox.Send(sharer, new ShareRequestCanceled(_request.Id));
        }

        // Se a troca já foi aceita, o transmissor continua encerrando; a sala fica ociosa depois.
        _request = null;
        return CommandResult.Ok;
    }

    /// <summary>Transmissor confirma que parou (voluntariamente ou atendendo a uma troca).</summary>
    internal CommandResult Stopped(ParticipantId actor, ShareSessionId session, Outbox outbox)
    {
        if (actor != Sharer)
        {
            return CommandResult.NotAllowed;
        }

        if (session != Session)
        {
            return CommandResult.UnknownRequest;
        }

        EndSession(outbox);
        return CommandResult.Ok;
    }

    /// <summary>Aplica o prazo indicado por <see cref="NextDeadline"/>; a sala só chama quando ele venceu.</summary>
    internal void ExpireDue(Outbox outbox)
    {
        if (HandoverInProgress)
        {
            // O transmissor não confirmou a tempo: a sessão é revogada e receptores passam a ignorá-la.
            EndSession(outbox);
            return;
        }

        if (_request is not null)
        {
            outbox.Send(_request.Requester, new ShareRequestExpired(_request.Id));
            if (Sharer is { } sharer)
            {
                outbox.Send(sharer, new ShareRequestExpired(_request.Id));
            }

            _request = null;
        }
    }

    internal void OnMemberRemoved(ParticipantId id, Outbox outbox)
    {
        if (_request?.Requester == id)
        {
            if (Sharer is { } sharer && sharer != id)
            {
                outbox.Send(sharer, new ShareRequestCanceled(_request.Id));
            }

            _request = null;
        }

        if (Sharer == id)
        {
            EndSession(outbox);
        }
    }

    /// <summary>O transmissor voltou: reentrega o que ele pode ter perdido durante a ausência.</summary>
    internal void OnResume(ParticipantId id, Outbox outbox)
    {
        if (Sharer != id)
        {
            return;
        }

        if (HandoverInProgress)
        {
            outbox.Send(id, new StopSharing(Session!.Value));
        }
        else if (_request is not null)
        {
            outbox.Send(id, new ShareRequested(_request.Id, _request.Requester, _request.ExpiresAt));
        }
    }

    internal void Reset()
    {
        Sharer = null;
        Session = null;
        _request = null;
        _handoverDeadline = null;
    }

    private void EndSession(Outbox outbox)
    {
        var next = _request?.Requester;
        Sharer = null;
        Session = null;
        _request = null;
        _handoverDeadline = null;

        if (next is { } requester)
        {
            Grant(requester, outbox);
        }
        else
        {
            Broadcast(new ShareStateChanged(null, null), outbox);
        }
    }

    private void Grant(ParticipantId sharer, Outbox outbox)
    {
        Sharer = sharer;
        Session = ShareSessionId.New();
        Broadcast(new ShareStateChanged(Sharer, Session), outbox);
    }

    private void Broadcast(ServerMessage message, Outbox outbox)
    {
        foreach (var id in _recipients())
        {
            outbox.Send(id, message);
        }
    }

    private sealed record PendingRequest(ShareRequestId Id, ParticipantId Requester, DateTimeOffset ExpiresAt);
}
