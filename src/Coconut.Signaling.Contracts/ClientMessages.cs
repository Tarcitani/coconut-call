using System.Text.Json.Serialization;

namespace Coconut.Signaling.Contracts;

/// <summary>Mensagem de um cliente para o servidor de sinalização.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CreateRoom), "createRoom")]
[JsonDerivedType(typeof(JoinRoom), "joinRoom")]
[JsonDerivedType(typeof(ResumeSession), "resumeSession")]
[JsonDerivedType(typeof(ReportLink), "reportLink")]
[JsonDerivedType(typeof(Relay), "relay")]
[JsonDerivedType(typeof(LeaveRoom), "leaveRoom")]
[JsonDerivedType(typeof(KickParticipant), "kick")]
[JsonDerivedType(typeof(EndRoomForAll), "endRoom")]
[JsonDerivedType(typeof(RequestShare), "requestShare")]
[JsonDerivedType(typeof(RespondToShareRequest), "respondShare")]
[JsonDerivedType(typeof(CancelShareRequest), "cancelShare")]
[JsonDerivedType(typeof(ShareStopped), "shareStopped")]
public abstract record ClientMessage;

// ---- Abertura de sessão (primeira mensagem da conexão) ----

public sealed record CreateRoom(string DisplayName) : ClientMessage;

public sealed record JoinRoom(string InviteCode, string DisplayName) : ClientMessage;

public sealed record ResumeSession(string ResumeToken) : ClientMessage;

// ---- Negociação P2P ----

public sealed record ReportLink(ParticipantId Peer, LinkStatus Status) : ClientMessage;

/// <summary>SDP ou candidato ICE para outro membro. O servidor só encaminha; nunca interpreta nem registra.</summary>
public sealed record Relay(ParticipantId To, RelayKind Kind, string Payload) : ClientMessage;

[JsonConverter(typeof(JsonStringEnumConverter<RelayKind>))]
public enum RelayKind
{
    Offer,
    Answer,
    Candidate,
}

// ---- Composição e administração ----

public sealed record LeaveRoom : ClientMessage;

public sealed record KickParticipant(ParticipantId Target) : ClientMessage;

public sealed record EndRoomForAll : ClientMessage;

// ---- Compartilhamento de tela ----

public sealed record RequestShare : ClientMessage;

public sealed record RespondToShareRequest(ShareRequestId Request, bool Accept) : ClientMessage;

public sealed record CancelShareRequest(ShareRequestId Request) : ClientMessage;

public sealed record ShareStopped(ShareSessionId Session) : ClientMessage;
