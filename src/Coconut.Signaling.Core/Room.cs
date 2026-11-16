using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core;

/// <summary>
/// Estado autoritativo de uma sala: composição, admissão P2P, administração e transmissor de tela.
/// Não é thread-safe por projeto: o host processa os comandos de uma sala sequencialmente.
/// Todo comando aplica antes os prazos vencidos em ordem cronológica, de modo que o resultado
/// não depende do ritmo com que o host chama <see cref="Tick"/>.
/// Especificação: docs/protocol/salas-e-admissao.md.
/// </summary>
public sealed partial class Room
{
    private readonly Dictionary<ParticipantId, Member> _members = [];
    private readonly HashSet<ParticipantId> _revoked = [];
    private readonly HashSet<ParticipantId> _usedIdentities = [];
    private readonly LinkTable _links = new();
    private readonly RoomOptions _options;
    private readonly TimeProvider _clock;
    private long _joinSequence;
    private long _admissionSequence;

    private Room(RoomOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
        Share = new ScreenShareArbiter(options, clock, () => Mesh().Select(m => m.Id).ToList());
    }

    public long Epoch { get; private set; }

    public ParticipantId? Admin { get; private set; }

    public bool IsClosed { get; private set; }

    public int MemberCount => _members.Count;

    /// <summary>Membros atuais (qualquer estado), na situação do último comando processado.</summary>
    public IReadOnlyList<ParticipantId> Members => _members.Keys.ToList();

    public ScreenShareArbiter Share { get; }

    /// <summary>Cria a sala com o criador já ativo e administrador (não há pares a verificar).</summary>
    public static Room Create(ParticipantId creator, string displayName, RoomOptions options, TimeProvider clock, Outbox outbox)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(outbox);

        var room = new Room(options, clock);
        var member = new Member(creator, displayName, ++room._joinSequence) { SeenJoinSequence = room._joinSequence };
        room._members.Add(creator, member);
        room._usedIdentities.Add(creator);
        room.Admit(member, outbox);
        return room;
    }

    /// <summary>Estado na situação do último comando processado (consulta; não aplica prazos).</summary>
    public ParticipantState? StateOf(ParticipantId id) => _members.TryGetValue(id, out var m) ? m.State : null;

    public CommandResult Join(ParticipantId id, string displayName, Outbox outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        AdvanceTime(outbox);
        if (IsClosed)
        {
            return CommandResult.RoomClosed;
        }

        if (_revoked.Contains(id))
        {
            return CommandResult.Revoked;
        }

        if (_members.ContainsKey(id))
        {
            return CommandResult.AlreadyMember;
        }

        if (_usedIdentities.Contains(id))
        {
            return CommandResult.IdentityReused;
        }

        if (_members.Count >= _options.Capacity)
        {
            outbox.Send(id, new RoomFull(_options.Capacity));
            return CommandResult.RoomFull;
        }

        var joiner = new Member(id, displayName, ++_joinSequence)
        {
            Deadline = _clock.GetUtcNow() + _options.AdmissionTimeout,
            SeenJoinSequence = _joinSequence,
        };

        var others = _members.Values.ToList();
        _members.Add(id, joiner);
        _usedIdentities.Add(id);

        // O recém-chegado é sempre o ofertante: entrou depois de todos os demais.
        outbox.Send(id, new AdmissionStarted(id, joiner.Deadline, others.Select(m => m.ToInfo()).ToList()));
        foreach (var other in others.Where(o => o.State != ParticipantState.Reconnecting))
        {
            outbox.Send(other.Id, new ConnectToPeer(joiner.ToInfo(), NegotiationRole.Answerer));
            other.SeenJoinSequence = _joinSequence;
        }

        EvaluateAdmissions(outbox);
        return CommandResult.Ok;
    }

    /// <summary>Autoriza o encaminhamento de SDP/ICE entre dois membros.</summary>
    public CommandResult CanRelay(ParticipantId from, ParticipantId to, Outbox outbox)
    {
        var guard = Guard(from, requireActive: false, outbox);
        if (guard != CommandResult.Ok)
        {
            return guard;
        }

        return from != to && _members.ContainsKey(to) ? CommandResult.Ok : CommandResult.InvalidTarget;
    }

    public CommandResult ReportLink(ParticipantId reporter, ParticipantId remote, LinkStatus status, Outbox outbox)
    {
        var guard = Guard(reporter, requireActive: false, outbox);
        if (guard != CommandResult.Ok)
        {
            return guard;
        }

        if (reporter == remote || !_members.ContainsKey(remote))
        {
            return CommandResult.InvalidTarget;
        }

        _links.Report(reporter, remote, status);
        EvaluateAdmissions(outbox);
        return CommandResult.Ok;
    }

    public CommandResult Leave(ParticipantId id, Outbox outbox)
    {
        var guard = Guard(id, requireActive: false, outbox);
        if (guard != CommandResult.Ok)
        {
            return guard;
        }

        var reason = _members[id].State == ParticipantState.Pending ? LeaveReason.AdmissionCanceled : LeaveReason.Left;
        Remove(id, reason, outbox);
        EvaluateAdmissions(outbox);
        return CommandResult.Ok;
    }

    /// <summary>A conexão de sinalização caiu. Não implica perda dos enlaces P2P.</summary>
    public void SignalingLost(ParticipantId id, Outbox outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        AdvanceTime(outbox);
        if (IsClosed || !_members.TryGetValue(id, out var member))
        {
            return;
        }

        switch (member.State)
        {
            case ParticipantState.Pending:
                Remove(id, LeaveReason.AdmissionCanceled, outbox);
                break;
            case ParticipantState.Active:
                member.State = ParticipantState.Reconnecting;
                member.Deadline = _clock.GetUtcNow() + _options.ReconnectGrace;
                BroadcastToMesh(new ParticipantReconnecting(id), outbox, except: id);
                break;
        }

        EvaluateAdmissions(outbox);
    }

    /// <summary>O participante voltou (nova conexão autenticada com o mesmo token de sessão).</summary>
    public CommandResult Resume(ParticipantId id, Outbox outbox)
    {
        var guard = Guard(id, requireActive: false, outbox);
        if (guard != CommandResult.Ok)
        {
            return guard;
        }

        var member = _members[id];
        if (member.State == ParticipantState.Pending)
        {
            return CommandResult.NotAllowed;
        }

        if (member.State == ParticipantState.Reconnecting)
        {
            member.State = ParticipantState.Active;
            BroadcastToMesh(new ParticipantResumed(id), outbox, except: id);
        }

        outbox.Send(id, Snapshot());

        // Só pendentes que chegaram durante a ausência: os anteriores já foram anunciados.
        foreach (var pending in _members.Values.Where(m => m.State == ParticipantState.Pending && m.JoinSequence > member.SeenJoinSequence))
        {
            outbox.Send(id, new ConnectToPeer(pending.ToInfo(), NegotiationRole.Answerer));
        }

        member.SeenJoinSequence = _joinSequence;
        Share.OnResume(id, outbox);
        EvaluateAdmissions(outbox);
        return CommandResult.Ok;
    }

    /// <summary>Aplica os prazos vencidos. O host chama periodicamente para entregar expirações sem esperar um comando.</summary>
    public void Tick(Outbox outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        AdvanceTime(outbox);
    }

    private IEnumerable<Member> Mesh() => _members.Values.Where(m => m.IsInMesh);

    private CommandResult Guard(ParticipantId actor, bool requireActive, Outbox outbox)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        AdvanceTime(outbox);
        if (IsClosed)
        {
            return CommandResult.RoomClosed;
        }

        if (_revoked.Contains(actor))
        {
            return CommandResult.Revoked;
        }

        if (!_members.TryGetValue(actor, out var member))
        {
            return CommandResult.NotAMember;
        }

        return requireActive && member.State != ParticipantState.Active ? CommandResult.NotAllowed : CommandResult.Ok;
    }

    private RoomSnapshot Snapshot() =>
        new(Epoch, Admin, _members.Values.Where(m => m.IsInMesh).Select(m => m.ToInfo()).ToList(), Share.Sharer, Share.Session);

    /// <summary>Estado da chamada vai só para a malha; pendentes recebem o retrato ao serem admitidos.</summary>
    private void BroadcastToMesh(ServerMessage message, Outbox outbox, ParticipantId? except = null)
    {
        foreach (var member in Mesh().Where(m => m.Id != except))
        {
            outbox.Send(member.Id, message);
        }
    }
}
