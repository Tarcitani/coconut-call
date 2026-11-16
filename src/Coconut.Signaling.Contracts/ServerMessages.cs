namespace Coconut.Signaling.Contracts;

/// <summary>Mensagem do servidor de sinalização para um cliente.</summary>
public abstract record ServerMessage;

// ---- Admissão e composição ----

public sealed record AdmissionStarted(ParticipantId You, DateTimeOffset Deadline, IReadOnlyList<PeerInfo> PeersToConnect) : ServerMessage;

public sealed record ConnectToPeer(PeerInfo Peer, NegotiationRole Role) : ServerMessage;

public sealed record PeerAdmitted(ParticipantId Admitted, long Epoch, IReadOnlyList<PeerInfo> Roster) : ServerMessage;

/// <summary>Falha de admissão. <paramref name="FailedPeers"/> traz o estado de cada par (ex.: em reconexão), nunca IPs.</summary>
public sealed record AdmissionFailed(AdmissionFailure Reason, IReadOnlyList<PeerInfo> FailedPeers) : ServerMessage;

/// <summary>Instrui o destinatário a fechar a conexão P2P com um participante que não chegou a ser admitido.</summary>
public sealed record DropPeer(ParticipantId Peer, LeaveReason Reason) : ServerMessage;

public sealed record PeerLeft(ParticipantId Peer, LeaveReason Reason, long Epoch) : ServerMessage;

public sealed record ParticipantReconnecting(ParticipantId Peer) : ServerMessage;

public sealed record ParticipantResumed(ParticipantId Peer) : ServerMessage;

public sealed record RoomSnapshot(
    long Epoch,
    ParticipantId? Admin,
    IReadOnlyList<PeerInfo> Roster,
    ParticipantId? Sharer,
    ShareSessionId? ShareSession) : ServerMessage;

public sealed record RoomFull(int Capacity) : ServerMessage;

// ---- Administração ----

public sealed record AdminChanged(ParticipantId Admin) : ServerMessage;

public sealed record Kicked : ServerMessage;

public sealed record RoomEnded : ServerMessage;

// ---- Compartilhamento de tela ----

/// <summary>Anúncio do transmissor vigente. Receptores descartam vídeo de qualquer outra sessão.</summary>
public sealed record ShareStateChanged(ParticipantId? Sharer, ShareSessionId? Session) : ServerMessage;

public sealed record ShareRequested(ShareRequestId Request, ParticipantId Requester, DateTimeOffset ExpiresAt) : ServerMessage;

public sealed record ShareRequestPending(ShareRequestId Request, DateTimeOffset ExpiresAt) : ServerMessage;

public sealed record ShareRequestDeclined(ShareRequestId Request) : ServerMessage;

public sealed record ShareRequestExpired(ShareRequestId Request) : ServerMessage;

public sealed record ShareRequestCanceled(ShareRequestId Request) : ServerMessage;

public sealed record ShareBusy : ServerMessage;

/// <summary>Instrui o transmissor atual a encerrar a sessão indicada (troca aceita).</summary>
public sealed record StopSharing(ShareSessionId Session) : ServerMessage;
