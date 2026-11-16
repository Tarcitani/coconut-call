using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Server.Tests;

public sealed class SignalingFlowTests : IAsyncDisposable
{
    private readonly SignalingTestHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Creating_room_establishes_session_with_invite_and_token()
    {
        await using var a = await _host.ConnectAsync();
        await a.SendAsync(new CreateRoom("Ana"));

        var session = await a.ExpectAsync<SessionEstablished>();
        var snapshot = await a.ExpectAsync<RoomSnapshot>();

        Assert.Equal(10, session.InviteCode.Length);
        Assert.True(session.ResumeToken.Length >= 40);
        Assert.Equal(session.You, snapshot.Admin);
    }

    [Fact]
    public async Task Two_participants_relay_negotiation_and_are_admitted()
    {
        await using var a = await _host.ConnectAsync();
        await using var b = await _host.ConnectAsync();
        var (sa, sb) = await AdmitPairAsync(a, b, relay: true);

        Assert.NotEqual(sa.You, sb.You);
    }

    [Fact]
    public async Task Unknown_invite_is_rejected_and_connection_closed()
    {
        await using var c = await _host.ConnectAsync();
        await c.SendAsync(new JoinRoom("ZZZZZZZZZZ", "Caio"));

        await c.ExpectAsync<InviteInvalid>();
        Assert.NotNull(await c.ExpectClosedAsync());
    }

    [Fact]
    public async Task Invite_typed_with_lowercase_and_dash_is_accepted()
    {
        await using var a = await _host.ConnectAsync();
        await a.SendAsync(new CreateRoom("Ana"));
        var invite = (await a.ExpectAsync<SessionEstablished>()).InviteCode;

        await using var b = await _host.ConnectAsync();
        await b.SendAsync(new JoinRoom(invite[..5].ToLowerInvariant() + "-" + invite[5..], "Bia"));

        await b.ExpectAsync<AdmissionStarted>();
    }

    [Fact]
    public async Task Relay_to_non_member_is_rejected()
    {
        await using var a = await _host.ConnectAsync();
        await a.SendAsync(new CreateRoom("Ana"));
        await a.ExpectAsync<RoomSnapshot>();

        await a.SendAsync(new Relay(ParticipantId.New(), RelayKind.Offer, "v=0"));

        var rejected = await a.ExpectAsync<CommandRejected>();
        Assert.Equal(("relay", "InvalidTarget"), (rejected.Command, rejected.Reason));
    }

    [Fact]
    public async Task Dropped_participant_resumes_same_identity_with_token()
    {
        await using var a = await _host.ConnectAsync();
        var b = await _host.ConnectAsync();
        var (_, sb) = await AdmitPairAsync(a, b, relay: false);

        b.Drop();
        await a.ExpectAsync<ParticipantReconnecting>(m => m.Peer == sb.You);

        await using var b2 = await _host.ConnectAsync();
        await b2.SendAsync(new ResumeSession(sb.ResumeToken));
        var resumed = await b2.ExpectAsync<SessionEstablished>();
        await b2.ExpectAsync<RoomSnapshot>();
        await a.ExpectAsync<ParticipantResumed>(m => m.Peer == sb.You);

        Assert.Equal(sb.You, resumed.You);
        await b.DisposeAsync();
    }

    [Fact]
    public async Task Kicked_participant_is_disconnected_and_cannot_resume()
    {
        await using var a = await _host.ConnectAsync();
        await using var b = await _host.ConnectAsync();
        var (_, sb) = await AdmitPairAsync(a, b, relay: false);

        await a.SendAsync(new KickParticipant(sb.You));

        await b.ExpectAsync<Kicked>();
        Assert.NotNull(await b.ExpectClosedAsync());

        await using var b2 = await _host.ConnectAsync();
        await b2.SendAsync(new ResumeSession(sb.ResumeToken));
        await b2.ExpectAsync<SessionInvalid>();
    }

    [Fact]
    public async Task Non_admin_cannot_kick()
    {
        await using var a = await _host.ConnectAsync();
        await using var b = await _host.ConnectAsync();
        var (sa, _) = await AdmitPairAsync(a, b, relay: false);

        await b.SendAsync(new KickParticipant(sa.You));

        var rejected = await b.ExpectAsync<CommandRejected>();
        Assert.Equal("NotAllowed", rejected.Reason);
    }

    [Fact]
    public async Task Last_member_leaving_closes_room_and_invalidates_invite()
    {
        await using var a = await _host.ConnectAsync();
        await a.SendAsync(new CreateRoom("Ana"));
        var invite = (await a.ExpectAsync<SessionEstablished>()).InviteCode;
        await a.ExpectAsync<RoomSnapshot>();

        await a.SendAsync(new LeaveRoom());
        await a.ExpectClosedAsync();

        await using var b = await _host.ConnectAsync();
        await b.SendAsync(new JoinRoom(invite, "Bia"));
        await b.ExpectAsync<InviteInvalid>();
    }

    [Fact]
    public async Task Screen_share_is_granted_and_announced_to_peer()
    {
        await using var a = await _host.ConnectAsync();
        await using var b = await _host.ConnectAsync();
        var (sa, _) = await AdmitPairAsync(a, b, relay: false);

        await a.SendAsync(new RequestShare());

        var announced = await b.ExpectAsync<ShareStateChanged>(m => m.Sharer is not null);
        Assert.Equal(sa.You, announced.Sharer);
    }

    [Fact]
    public async Task Logs_never_contain_secrets_names_or_negotiation_payloads()
    {
        const string sdpMarker = "a=candidate:1 1 udp 2122260223 203.0.113.7 54400 typ host";
        await using var a = await _host.ConnectAsync();
        await using var b = await _host.ConnectAsync();
        await a.SendAsync(new CreateRoom("NomeSecretoAna"));
        var sa = await a.ExpectAsync<SessionEstablished>();
        await b.SendAsync(new JoinRoom(sa.InviteCode, "NomeSecretoBia"));
        var sb = await b.ExpectAsync<SessionEstablished>();
        await b.SendAsync(new Relay(sa.You, RelayKind.Candidate, sdpMarker));
        await a.ExpectAsync<Relayed>();
        await a.SendAsync(new KickParticipant(sb.You));
        await b.ExpectClosedAsync();
        await a.SendAsync(new EndRoomForAll());
        await a.ExpectClosedAsync();

        var logs = string.Join('\n', _host.Logs);
        Assert.Contains("criada", logs, StringComparison.Ordinal); // a captura de logs funciona
        foreach (var secret in new[] { sa.InviteCode, sa.ResumeToken, sb.ResumeToken, "NomeSecretoAna", "NomeSecretoBia", "203.0.113.7", "candidate" })
        {
            Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Cria a sala com <paramref name="a"/>, faz <paramref name="b"/> entrar e conclui a admissão.</summary>
    private static async Task<(SessionEstablished A, SessionEstablished B)> AdmitPairAsync(TestClient a, TestClient b, bool relay)
    {
        await a.SendAsync(new CreateRoom("Ana"));
        var sa = await a.ExpectAsync<SessionEstablished>();
        await a.ExpectAsync<RoomSnapshot>();

        await b.SendAsync(new JoinRoom(sa.InviteCode, "Bia"));
        var sb = await b.ExpectAsync<SessionEstablished>();
        var started = await b.ExpectAsync<AdmissionStarted>();
        Assert.Contains(started.PeersToConnect, p => p.Id == sa.You);

        var connect = await a.ExpectAsync<ConnectToPeer>();
        Assert.Equal((sb.You, NegotiationRole.Answerer), (connect.Peer.Id, connect.Role));

        if (relay)
        {
            await b.SendAsync(new Relay(sa.You, RelayKind.Offer, "oferta-opaca"));
            var offer = await a.ExpectAsync<Relayed>();
            Assert.Equal((sb.You, RelayKind.Offer, "oferta-opaca"), (offer.From, offer.Kind, offer.Payload));

            await a.SendAsync(new Relay(sb.You, RelayKind.Answer, "resposta-opaca"));
            var answer = await b.ExpectAsync<Relayed>();
            Assert.Equal((sa.You, RelayKind.Answer), (answer.From, answer.Kind));
        }

        await a.SendAsync(new ReportLink(sb.You, LinkStatus.Verified));
        await b.SendAsync(new ReportLink(sa.You, LinkStatus.Verified));
        await a.ExpectAsync<PeerAdmitted>(m => m.Admitted == sb.You);
        await b.ExpectAsync<PeerAdmitted>(m => m.Admitted == sb.You);
        await b.ExpectAsync<RoomSnapshot>();
        return (sa, sb);
    }
}
