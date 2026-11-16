using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Server.Tests;

/// <summary>Toda mensagem do protocolo precisa sobreviver a serializar → desserializar.</summary>
public class ProtocolJsonTests
{
    private static readonly ParticipantId Someone = ParticipantId.New();
    private static readonly PeerInfo Peer = new(Someone, "Ana", ParticipantState.Active);

    public static TheoryData<ClientMessage> ClientMessages() =>
    [
        new CreateRoom("Ana"),
        new JoinRoom("ABCDE12345", "Bia"),
        new ResumeSession("token"),
        new ReportLink(Someone, LinkStatus.Failed),
        new Relay(Someone, RelayKind.Candidate, "candidate"),
        new LeaveRoom(),
        new KickParticipant(Someone),
        new EndRoomForAll(),
        new RequestShare(),
        new RespondToShareRequest(ShareRequestId.New(), true),
        new CancelShareRequest(ShareRequestId.New()),
        new ShareStopped(ShareSessionId.New()),
    ];

    public static TheoryData<ServerMessage> ServerMessages() =>
    [
        new SessionEstablished(Someone, "token", "ABCDE12345"),
        new InviteInvalid(),
        new SessionInvalid(),
        new CommandRejected("relay", "InvalidTarget"),
        new Relayed(Someone, RelayKind.Offer, "v=0"),
        new AdmissionStarted(Someone, DateTimeOffset.UnixEpoch, [Peer]),
        new ConnectToPeer(Peer, NegotiationRole.Answerer),
        new PeerAdmitted(Someone, 3, [Peer]),
        new AdmissionFailed(AdmissionFailure.Timeout, [Peer]),
        new DropPeer(Someone, LeaveReason.Kicked),
        new PeerLeft(Someone, LeaveReason.TimedOut, 4),
        new ParticipantReconnecting(Someone),
        new ParticipantResumed(Someone),
        new RoomSnapshot(5, Someone, [Peer], null, null),
        new RoomFull(15),
        new AdminChanged(Someone),
        new Kicked(),
        new RoomEnded(),
        new ShareStateChanged(Someone, ShareSessionId.New()),
        new ShareRequested(ShareRequestId.New(), Someone, DateTimeOffset.UnixEpoch),
        new ShareRequestPending(ShareRequestId.New(), DateTimeOffset.UnixEpoch),
        new ShareRequestDeclined(ShareRequestId.New()),
        new ShareRequestExpired(ShareRequestId.New()),
        new ShareRequestCanceled(ShareRequestId.New()),
        new ShareBusy(),
        new StopSharing(ShareSessionId.New()),
    ];

    [Theory]
    [MemberData(nameof(ClientMessages))]
    public void Client_message_round_trips(ClientMessage message)
    {
        var json = SignalingJson.Serialize(message);

        Assert.Equal(json, SignalingJson.Serialize(SignalingJson.DeserializeClient(System.Text.Encoding.UTF8.GetBytes(json))!));
        Assert.Contains("\"type\":", json, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ServerMessages))]
    public void Server_message_round_trips(ServerMessage message)
    {
        var json = SignalingJson.Serialize(message);

        Assert.Equal(json, SignalingJson.Serialize(SignalingJson.DeserializeServer(json)!));
    }

    [Fact]
    public void Every_message_type_is_covered_by_round_trip_tests()
    {
        IEnumerable<object> messages = [.. ClientMessages(), .. ServerMessages()];
        var covered = messages.Select(m => m.GetType()).ToHashSet();
        var all = typeof(ServerMessage).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && (t.IsSubclassOf(typeof(ServerMessage)) || t.IsSubclassOf(typeof(ClientMessage))));

        Assert.Empty(all.Where(t => !covered.Contains(t)).Select(t => t.Name));
    }

    [Fact]
    public void Identifiers_are_plain_strings_on_the_wire()
    {
        var json = SignalingJson.Serialize(new KickParticipant(Someone));

        Assert.Contains($"\"target\":\"{Someone.Value}\"", json, StringComparison.Ordinal);
    }
}
