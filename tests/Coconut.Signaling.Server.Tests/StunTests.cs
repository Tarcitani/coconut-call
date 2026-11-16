using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Coconut.Signaling.Server.Stun;

namespace Coconut.Signaling.Server.Tests;

public class StunMessageTests
{
    private static readonly byte[] TransactionId = [0xb7, 0xe7, 0xa7, 0x01, 0xbc, 0x34, 0xd6, 0x86, 0xfa, 0x87, 0xdf, 0xae];

    [Fact]
    public void Valid_binding_request_is_accepted_and_transaction_id_returned()
    {
        var request = StunMessage.CreateBindingRequest(TransactionId);

        Assert.True(StunMessage.TryParseBindingRequest(request, out var id));
        Assert.Equal(TransactionId, id.ToArray());
    }

    [Fact]
    public void Request_with_attributes_is_accepted_when_length_matches()
    {
        var request = new byte[StunMessage.HeaderLength + 8];
        StunMessage.CreateBindingRequest(TransactionId).CopyTo(request, 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2), 8); // ex.: SOFTWARE/FINGERPRINT enviados pelo cliente

        Assert.True(StunMessage.TryParseBindingRequest(request, out _));
    }

    public static TheoryData<string, byte[]> Malformed()
    {
        var valid = StunMessage.CreateBindingRequest(TransactionId);
        byte[] With(Action<byte[]> change)
        {
            var copy = valid.ToArray();
            change(copy);
            return copy;
        }

        return new TheoryData<string, byte[]>
        {
            { "curta", valid[..19] },
            { "cookie errado", With(b => b[4] = 0) },
            { "tipo não é Binding Request", With(b => b[1] = 0x02) },
            { "primeiros bits não zero (RTP/DTLS)", With(b => b[0] = 0x80) },
            { "tamanho declarado diferente do real", With(b => b[3] = 4) },
            { "tamanho não múltiplo de 4", [.. With(b => b[3] = 2), 0, 0] },
        };
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Malformed_messages_are_rejected(string _, byte[] message) =>
        Assert.False(StunMessage.TryParseBindingRequest(message, out var __));

    [Fact]
    public void Ipv4_response_matches_rfc5769_xor_values()
    {
        var response = new byte[StunMessage.MaxResponseLength];
        var length = StunMessage.WriteBindingSuccess(response, TransactionId, new IPEndPoint(IPAddress.Parse("192.0.2.1"), 32853));

        // Calculado à mão: 32853 ^ 0x2112 = 0xA147; 0xC0000201 ^ 0x2112A442 = 0xE112A643 (RFC 5769 §2.2).
        Assert.Equal(32, length);
        Assert.Equal(0x0101, BinaryPrimitives.ReadUInt16BigEndian(response));
        Assert.Equal(12, BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2)));
        Assert.Equal(TransactionId, response.AsSpan(8, 12).ToArray());
        Assert.Equal(0x0020, BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(20)));
        Assert.Equal(0x01, response[25]);
        Assert.Equal(0xA147, BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(26)));
        Assert.Equal(0xE112A643u, BinaryPrimitives.ReadUInt32BigEndian(response.AsSpan(28)));
    }

    [Theory]
    [InlineData("203.0.113.9", 3478)]
    [InlineData("2001:db8::1234:5678", 50000)]
    public void Response_round_trips_the_mapped_endpoint(string address, int port)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(address), port);
        var response = new byte[StunMessage.MaxResponseLength];
        var length = StunMessage.WriteBindingSuccess(response, TransactionId, endpoint);

        Assert.Equal(endpoint, StunMessage.ReadMappedAddress(response.AsSpan(0, length)));
    }

    [Fact]
    public void Ipv4_mapped_ipv6_source_is_reported_as_ipv4()
    {
        var response = new byte[StunMessage.MaxResponseLength];
        var length = StunMessage.WriteBindingSuccess(response, TransactionId, new IPEndPoint(IPAddress.Parse("::ffff:198.51.100.7"), 1234));

        Assert.Equal(32, length);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("198.51.100.7"), 1234), StunMessage.ReadMappedAddress(response.AsSpan(0, length)));
    }
}

public sealed class StunResponderTests : IAsyncDisposable
{
    private readonly SignalingTestHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private static async Task<byte[]?> ExchangeAsync(UdpClient client, IPEndPoint server, byte[] request, int timeoutMs = 1000)
    {
        await client.SendAsync(request, server);
        using var timeout = new CancellationTokenSource(timeoutMs);
        try
        {
            return (await client.ReceiveAsync(timeout.Token)).Buffer;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    [Fact]
    public async Task Answers_with_the_observed_ipv4_source()
    {
        var port = await _host.StunPortAsync();
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var id = Guid.NewGuid().ToByteArray()[..12];

        var response = await ExchangeAsync(client, new IPEndPoint(IPAddress.Loopback, port), StunMessage.CreateBindingRequest(id));

        Assert.NotNull(response);
        Assert.Equal(id, response.AsSpan(8, 12).ToArray());
        Assert.Equal(client.Client.LocalEndPoint, StunMessage.ReadMappedAddress(response));
    }

    [Fact]
    public async Task Answers_over_ipv6()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return;
        }

        var port = await _host.StunPortAsync();
        using var client = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0));

        var response = await ExchangeAsync(client, new IPEndPoint(IPAddress.IPv6Loopback, port), StunMessage.CreateBindingRequest(new byte[12]));

        Assert.NotNull(response);
        Assert.Equal(client.Client.LocalEndPoint, StunMessage.ReadMappedAddress(response));
    }

    [Fact]
    public async Task Ignores_non_stun_traffic()
    {
        var port = await _host.StunPortAsync();
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));

        var response = await ExchangeAsync(client, new IPEndPoint(IPAddress.Loopback, port), [0x80, 0x6F, 0x00, 0x01], timeoutMs: 300);

        Assert.Null(response);
    }

    [Fact]
    public async Task Rate_limits_each_source()
    {
        await using var host = new SignalingTestHost(o => o.Stun.RequestsPerSecondPerAddress = 5);
        var port = await host.StunPortAsync();
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var server = new IPEndPoint(IPAddress.Loopback, port);

        for (var i = 0; i < 30; i++)
        {
            await client.SendAsync(StunMessage.CreateBindingRequest(new byte[12]), server);
        }

        var answered = 0;
        using var window = new CancellationTokenSource(500);
        try
        {
            while (true)
            {
                await client.ReceiveAsync(window.Token);
                answered++;
            }
        }
        catch (OperationCanceledException)
        {
        }

        Assert.InRange(answered, 5, 7); // rajada de 5 + reposição durante o envio
    }
}
