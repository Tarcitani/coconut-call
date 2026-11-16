using System.Text.Json.Serialization;

namespace Coconut.Signaling.Contracts;

/// <summary>Mensagem do servidor de sinalização para um cliente.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionEstablished), "sessionEstablished")]
[JsonDerivedType(typeof(InviteInvalid), "inviteInvalid")]
[JsonDerivedType(typeof(SessionInvalid), "sessionInvalid")]
[JsonDerivedType(typeof(CommandRejected), "commandRejected")]
[JsonDerivedType(typeof(Relayed), "relayed")]
[JsonDerivedType(typeof(AdmissionStarted), "admissionStarted")]
[JsonDerivedType(typeof(ConnectToPeer), "connectToPeer")]
[JsonDerivedType(typeof(PeerAdmitted), "peerAdmitted")]
[JsonDerivedType(typeof(AdmissionFailed), "admissionFailed")]
[JsonDerivedType(typeof(DropPeer), "dropPeer")]
[JsonDerivedType(typeof(PeerLeft), "peerLeft")]
[JsonDerivedType(typeof(ParticipantReconnecting), "participantReconnecting")]
[JsonDerivedType(typeof(ParticipantResumed), "participantResumed")]
[JsonDerivedType(typeof(RoomSnapshot), "roomSnapshot")]
[JsonDerivedType(typeof(RoomFull), "roomFull")]
[JsonDerivedType(typeof(AdminChanged), "adminChanged")]
[JsonDerivedType(typeof(Kicked), "kicked")]
[JsonDerivedType(typeof(RoomEnded), "roomEnded")]
[JsonDerivedType(typeof(ShareStateChanged), "shareStateChanged")]
[JsonDerivedType(typeof(ShareRequested), "shareRequested")]
[JsonDerivedType(typeof(ShareRequestPending), "shareRequestPending")]
[JsonDerivedType(typeof(ShareRequestDeclined), "shareRequestDeclined")]
[JsonDerivedType(typeof(ShareRequestExpired), "shareRequestExpired")]
[JsonDerivedType(typeof(ShareRequestCanceled), "shareRequestCanceled")]
[JsonDerivedType(typeof(ShareBusy), "shareBusy")]
[JsonDerivedType(typeof(StopSharing), "stopSharing")]
public abstract record ServerMessage;

// ---- Sessão ----

/// <summary>
/// Resposta a CreateRoom/JoinRoom/ResumeSession. O token de retomada é segredo do cliente:
/// permite voltar à mesma identidade dentro da tolerância de reconexão.
/// </summary>
public sealed record SessionEstablished(ParticipantId You, string ResumeToken, string InviteCode) : ServerMessage;

/// <summary>Convite inexistente ou de sala encerrada (indistinguíveis de propósito).</summary>
public sealed record InviteInvalid : ServerMessage;

/// <summary>Token de retomada desconhecido, expirado ou revogado.</summary>
public sealed record SessionInvalid : ServerMessage;

/// <summary>Comando recusado. <paramref name="Reason"/> é um código estável (ex.: <c>NotAllowed</c>).</summary>
public sealed record CommandRejected(string Command, string Reason) : ServerMessage;

/// <summary>SDP ou candidato ICE encaminhado de outro membro.</summary>
public sealed record Relayed(ParticipantId From, RelayKind Kind, string Payload) : ServerMessage;

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
