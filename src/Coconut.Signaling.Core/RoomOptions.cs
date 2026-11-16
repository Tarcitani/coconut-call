namespace Coconut.Signaling.Core;

/// <summary>Parâmetros de uma sala. Os padrões são hipóteses iniciais, a calibrar com medições.</summary>
public sealed record RoomOptions
{
    /// <summary>Requisito do produto: até 15 participantes (pendentes incluídos).</summary>
    public int Capacity { get; init; } = 15;

    public TimeSpan AdmissionTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan ReconnectGrace { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan ShareRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan HandoverTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Resultado de um comando de cliente. Nunca lança exceção para entrada inválida de cliente.</summary>
public enum CommandResult
{
    Ok,
    RoomClosed,
    RoomFull,
    NotAMember,
    Revoked,
    NotAllowed,
    InvalidTarget,
    AlreadyMember,

    /// <summary>Identidade já usada nesta sala; voltar exige nova identidade.</summary>
    IdentityReused,
    AlreadySharing,
    Busy,
    UnknownRequest,
}
