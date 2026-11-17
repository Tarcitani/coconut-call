using System.Net.WebSockets;
using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Server.Tests;

/// <summary>Limites de proteção do servidor: handshake, tamanho, taxa, formato e salas por origem.</summary>
public sealed class ProtectionTests
{
    [Fact]
    public async Task First_message_must_open_a_session()
    {
        await using var host = new SignalingTestHost();
        await using var c = await host.ConnectAsync();

        await c.SendAsync(new ReportLink(ParticipantId.New(), LinkStatus.Verified));

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await c.ExpectClosedAsync());
    }

    [Fact]
    public async Task Silent_connection_is_closed_after_handshake_timeout()
    {
        await using var host = new SignalingTestHost(o => o.HandshakeTimeout = TimeSpan.FromMilliseconds(300));
        await using var c = await host.ConnectAsync();

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await c.ExpectClosedAsync());
    }

    [Fact]
    public async Task Oversized_message_closes_connection()
    {
        await using var host = new SignalingTestHost(o => o.MaxMessageBytes = 8 * 1024);
        await using var c = await host.ConnectAsync();
        await c.SendAsync(new CreateRoom("Ana"));
        await c.ExpectAsync<RoomSnapshot>();

        await c.SendRawAsync(new string('x', 20 * 1024));

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, await c.ExpectClosedAsync());
    }

    [Fact]
    public async Task Malformed_json_closes_connection()
    {
        await using var host = new SignalingTestHost();
        await using var c = await host.ConnectAsync();
        await c.SendAsync(new CreateRoom("Ana"));
        await c.ExpectAsync<RoomSnapshot>();

        await c.SendRawAsync("{\"type\":\"relay\",\"to\":");

        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, await c.ExpectClosedAsync());
    }

    [Fact]
    public async Task Unknown_message_type_closes_connection()
    {
        await using var host = new SignalingTestHost();
        await using var c = await host.ConnectAsync();

        await c.SendRawAsync("{\"type\":\"becomeAdmin\"}");

        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, await c.ExpectClosedAsync());
    }

    [Fact]
    public async Task Message_flood_closes_connection()
    {
        await using var host = new SignalingTestHost(o =>
        {
            o.MessageBurst = 5;
            o.MessagesPerSecond = 1;
        });
        await using var c = await host.ConnectAsync();
        await c.SendAsync(new CreateRoom("Ana"));
        await c.ExpectAsync<RoomSnapshot>();

        for (var i = 0; i < 20; i++)
        {
            await c.SendAsync(new ReportLink(ParticipantId.New(), LinkStatus.Verified));
        }

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, await c.ExpectClosedAsync());
        Assert.Equal("rate-limit", c.CloseDescription);
    }

    [Fact]
    public async Task Invalid_display_name_is_rejected()
    {
        await using var host = new SignalingTestHost();
        foreach (var name in new[] { "", "   ", new string('n', 33), "a\u0007b" })
        {
            await using var c = await host.ConnectAsync();
            await c.SendAsync(new CreateRoom(name));
            Assert.Equal("InvalidDisplayName", (await c.ExpectAsync<CommandRejected>()).Reason);
        }
    }

    [Fact]
    public async Task Rooms_per_origin_are_limited()
    {
        await using var host = new SignalingTestHost(o => o.MaxRoomsPerAddress = 1);
        await using var first = await host.ConnectAsync();
        await first.SendAsync(new CreateRoom("Ana"));
        await first.ExpectAsync<RoomSnapshot>();

        await using var second = await host.ConnectAsync();
        await second.SendAsync(new CreateRoom("Ana"));

        Assert.Equal("RoomLimit", (await second.ExpectAsync<CommandRejected>()).Reason);
    }

    /// <summary>
    /// Regressão (encontrada na VM, 2026-11-16): a contagem voltava a 0 mas a chave ficava, e a criação
    /// seguinte do mesmo endereço entrava em laço infinito (TryAdd falhava para sempre), girando uma thread.
    /// </summary>
    [Fact]
    public async Task Room_slot_is_released_when_the_room_closes()
    {
        await using var host = new SignalingTestHost(o => o.MaxRoomsPerAddress = 1);
        for (var i = 0; i < 3; i++)
        {
            await using var creator = await host.ConnectAsync();
            await creator.SendAsync(new CreateRoom("Ana"));
            await creator.ExpectAsync<RoomSnapshot>();
            await creator.SendAsync(new LeaveRoom());
            await creator.ExpectClosedAsync();
        }
    }

    [Theory]
    [InlineData("203.0.113.9", "http", 403)]
    [InlineData("203.0.113.9", "https", 200)]
    [InlineData("127.0.0.1", "http", 200)]
    [InlineData("::1", "http", 200)]
    public async Task Plain_http_is_refused_unless_from_loopback(string remote, string scheme, int expected)
    {
        await using var host = new SignalingTestHost();

        var context = await host.SendHttpAsync(c =>
        {
            c.Request.Path = "/healthz";
            c.Request.Scheme = scheme;
            c.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remote);
        });

        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Theory]
    // Proxy de loopback informa cliente externo em HTTPS → aceito (é o caso Caddy + TLS).
    [InlineData("127.0.0.1", "203.0.113.9", "https", 200)]
    // Proxy de loopback informa cliente externo em HTTP → recusado: prova que o IP encaminhado foi aplicado.
    [InlineData("127.0.0.1", "203.0.113.9", "http", 403)]
    // Cliente externo direto tentando forjar loopback/HTTPS → cabeçalhos ignorados, recusado.
    [InlineData("198.51.100.1", "127.0.0.1", "https", 403)]
    public async Task Forwarded_headers_are_trusted_only_from_loopback_proxy(string remote, string forwardedFor, string forwardedProto, int expected)
    {
        await using var host = new SignalingTestHost();

        var context = await host.SendHttpAsync(c =>
        {
            c.Request.Path = "/healthz";
            c.Request.Scheme = "http";
            c.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remote);
            c.Request.Headers["X-Forwarded-For"] = forwardedFor;
            c.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        });

        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Fact]
    public async Task Unknown_resume_token_is_rejected()
    {
        await using var host = new SignalingTestHost();
        await using var c = await host.ConnectAsync();

        await c.SendAsync(new ResumeSession("token-inventado"));

        await c.ExpectAsync<SessionInvalid>();
    }
}
