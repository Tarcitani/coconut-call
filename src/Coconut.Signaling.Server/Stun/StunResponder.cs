using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Coconut.Signaling.Server.Stun;

public sealed class StunOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Porta UDP (3478 é a padrão do STUN). 0 = efêmera (testes).</summary>
    public int Port { get; set; } = 3478;

    /// <summary>Limite por endereço de origem: barra o uso do servidor como refletor.</summary>
    public int RequestsPerSecondPerAddress { get; set; } = 20;
}

/// <summary>
/// Servidor STUN mínimo (RFC 5389, só Binding) junto à sinalização, para que clientes atrás de NAT descubram
/// o endereço público sem depender de terceiros. Dual-stack (IPv4 e IPv6). Nunca registra endereços.
/// </summary>
internal sealed partial class StunResponder(SignalingOptions options, TimeProvider clock, ILogger<StunResponder> log) : BackgroundService
{
    private readonly ConcurrentDictionary<IPAddress, Bucket> _buckets = new();
    private readonly TaskCompletionSource<int> _bound = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _answered;
    private long _dropped;

    /// <summary>Porta efetivamente aberta (útil com Port = 0).</summary>
    public Task<int> BoundPort => _bound.Task;

    public long Answered => Interlocked.Read(ref _answered);

    public long Dropped => Interlocked.Read(ref _dropped);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Stun.Enabled)
        {
            _bound.TrySetResult(0);
            return;
        }

        using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Stun.Port));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        _bound.TrySetResult(port);
        LogListening(log, port);

        var buffer = new byte[548]; // Binding Request real é bem menor; acima disso é descartado
        var response = new byte[StunMessage.MaxResponseLength];
        EndPoint any = new IPEndPoint(IPAddress.IPv6Any, 0);
        var lastSweep = clock.GetTimestamp();

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue; // ex.: ICMP port unreachable de uma resposta anterior (Windows)
            }

            var source = (IPEndPoint)received.RemoteEndPoint;
            if (!StunMessage.TryParseBindingRequest(buffer.AsSpan(0, received.ReceivedBytes), out var transactionId)
                || !Allow(source.Address))
            {
                Interlocked.Increment(ref _dropped);
                continue;
            }

            var length = StunMessage.WriteBindingSuccess(response, transactionId, source);
            try
            {
                await socket.SendToAsync(response.AsMemory(0, length), SocketFlags.None, source, stoppingToken);
                Interlocked.Increment(ref _answered);
            }
            catch (SocketException)
            {
                Interlocked.Increment(ref _dropped);
            }

            if (clock.GetElapsedTime(lastSweep) > TimeSpan.FromMinutes(1))
            {
                Sweep();
                lastSweep = clock.GetTimestamp();
            }
        }
    }

    private bool Allow(IPAddress address)
    {
        var bucket = _buckets.GetOrAdd(address, _ => new Bucket(options.Stun.RequestsPerSecondPerAddress, clock.GetTimestamp()));
        lock (bucket)
        {
            var now = clock.GetTimestamp();
            bucket.Tokens = Math.Min(options.Stun.RequestsPerSecondPerAddress, bucket.Tokens + (clock.GetElapsedTime(bucket.Last, now).TotalSeconds * options.Stun.RequestsPerSecondPerAddress));
            bucket.Last = now;
            if (bucket.Tokens < 1)
            {
                return false;
            }

            bucket.Tokens -= 1;
            return true;
        }
    }

    /// <summary>Esquece origens inativas, para a tabela não crescer sem limite.</summary>
    private void Sweep()
    {
        foreach (var (address, bucket) in _buckets)
        {
            if (clock.GetElapsedTime(bucket.Last) > TimeSpan.FromMinutes(1))
            {
                _buckets.TryRemove(address, out _);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "STUN escutando na porta UDP {Port}")]
    private static partial void LogListening(ILogger logger, int port);

    private sealed class Bucket(double tokens, long last)
    {
        public double Tokens { get; set; } = tokens;

        public long Last { get; set; } = last;
    }
}
