using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core;

internal sealed class Member(ParticipantId id, string displayName, long joinSequence)
{
    public ParticipantId Id { get; } = id;

    public string DisplayName { get; } = displayName;

    /// <summary>Ordem de entrada; quem entrou depois é o ofertante do par.</summary>
    public long JoinSequence { get; } = joinSequence;

    /// <summary>Ordem de admissão; define a antiguidade para sucessão. Zero enquanto pendente.</summary>
    public long AdmissionSequence { get; set; }

    public ParticipantState State { get; set; } = ParticipantState.Pending;

    /// <summary>
    /// Última sequência de entrada que este membro recebeu enquanto conectado; ao retomar,
    /// só pendentes que entraram depois recebem um novo <c>ConnectToPeer</c>.
    /// </summary>
    public long SeenJoinSequence { get; set; }

    /// <summary>Prazo da admissão (Pending) ou da tolerância de reconexão (Reconnecting).</summary>
    public DateTimeOffset Deadline { get; set; }

    /// <summary>Membros que contam no conjunto ativo de decisão da admissão.</summary>
    public bool IsInMesh => State is ParticipantState.Active or ParticipantState.Reconnecting;

    public PeerInfo ToInfo() => new(Id, DisplayName, State);
}
