namespace Coconut.Signaling.Contracts;

/// <summary>Identidade temporária de um participante dentro de uma sala. Não é uma conta.</summary>
public readonly record struct ParticipantId(Guid Value)
{
    public static ParticipantId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>Identifica uma sessão de transmissão de tela concedida pelo servidor.</summary>
public readonly record struct ShareSessionId(Guid Value)
{
    public static ShareSessionId New() => new(Guid.NewGuid());
}

/// <summary>Identifica um pedido de troca de transmissor.</summary>
public readonly record struct ShareRequestId(Guid Value)
{
    public static ShareRequestId New() => new(Guid.NewGuid());
}

/// <summary>Estado de um enlace P2P reportado por uma das extremidades.</summary>
public enum LinkStatus
{
    /// <summary>ICE + DTLS concluídos e eco no canal de dados confirmado.</summary>
    Verified,

    /// <summary>Negociação ou conexão falhou de forma terminal.</summary>
    Failed,
}

/// <summary>Quem envia a oferta SDP num par. Determinístico para evitar glare.</summary>
public enum NegotiationRole
{
    Offerer,
    Answerer,
}

public enum LeaveReason
{
    Left,
    Kicked,
    TimedOut,
    AdmissionFailed,
    AdmissionCanceled,
}

public enum AdmissionFailure
{
    Timeout,
    PeerLinkFailed,

    /// <summary>Todos os participantes admitidos saíram; a sala foi encerrada.</summary>
    RoomEmptied,
}

public enum ParticipantState
{
    Pending,
    Active,
    Reconnecting,
}

public sealed record PeerInfo(ParticipantId Id, string DisplayName, ParticipantState State);
