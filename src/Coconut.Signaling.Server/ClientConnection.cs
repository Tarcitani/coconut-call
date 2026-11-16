using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Server;

/// <summary>
/// Uma conexão WebSocket: recepção com limite de tamanho e de taxa, e envio serializado por
/// uma fila própria (WebSocket não admite envios concorrentes).
/// </summary>
internal sealed class ClientConnection
{
    private readonly WebSocket _socket;
    private readonly SignalingOptions _options;
    private readonly TimeProvider _clock;
    private readonly Channel<ServerMessage> _outgoing;
    private double _tokens;
    private long _lastRefill;

    public ClientConnection(WebSocket socket, SignalingOptions options, TimeProvider clock)
    {
        _socket = socket;
        _options = options;
        _clock = clock;
        _outgoing = Channel.CreateBounded<ServerMessage>(new BoundedChannelOptions(options.OutgoingQueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _tokens = options.MessageBurst;
        _lastRefill = clock.GetTimestamp();
    }

    /// <summary>Motivo do fechamento iniciado pelo servidor, quando houver.</summary>
    public (WebSocketCloseStatus Status, string Description)? CloseReason { get; private set; }

    /// <summary>Enfileira sem bloquear. Fila cheia = cliente lento: a conexão é encerrada.</summary>
    public bool TrySend(ServerMessage message)
    {
        if (_outgoing.Writer.TryWrite(message))
        {
            return true;
        }

        Close(WebSocketCloseStatus.PolicyViolation, "slow-consumer");
        return false;
    }

    /// <summary>Fecha depois de entregar o que já está na fila.</summary>
    public void Close(WebSocketCloseStatus status = WebSocketCloseStatus.NormalClosure, string description = "bye")
    {
        CloseReason ??= (status, description);
        _outgoing.Writer.TryComplete();
    }

    public async Task RunWriterAsync(CancellationToken cancellation)
    {
        try
        {
            await foreach (var message in _outgoing.Reader.ReadAllAsync(cancellation))
            {
                var bytes = Encoding.UTF8.GetBytes(SignalingJson.Serialize(message));
                await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellation);
            }

            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                var (status, description) = CloseReason ?? (WebSocketCloseStatus.NormalClosure, "bye");
                await _socket.CloseOutputAsync(status, description, cancellation);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // Conexão já caiu; o laço de recepção trata a perda de sinalização.
        }
    }

    /// <summary>Próxima mensagem do cliente, ou nulo se a conexão terminou ou violou o protocolo.</summary>
    public async Task<ClientMessage?> ReceiveAsync(CancellationToken cancellation)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(_options.MaxMessageBytes + 1);
        try
        {
            var length = 0;
            while (true)
            {
                if (length > _options.MaxMessageBytes)
                {
                    Close(WebSocketCloseStatus.MessageTooBig, "message-too-big");
                    return null;
                }

                var result = await _socket.ReceiveAsync(buffer.AsMemory(length, Math.Min(buffer.Length - length, 4096)), cancellation);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    Close(WebSocketCloseStatus.InvalidMessageType, "text-only");
                    return null;
                }

                length += result.Count;
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            if (length > _options.MaxMessageBytes)
            {
                Close(WebSocketCloseStatus.MessageTooBig, "message-too-big");
                return null;
            }

            if (!ConsumeRateToken())
            {
                Close(WebSocketCloseStatus.PolicyViolation, "rate-limit");
                return null;
            }

            try
            {
                return SignalingJson.DeserializeClient(buffer.AsSpan(0, length))
                    ?? throw new JsonException("mensagem vazia");
            }
            catch (JsonException)
            {
                Close(WebSocketCloseStatus.InvalidPayloadData, "malformed");
                return null;
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private bool ConsumeRateToken()
    {
        var now = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedTime(_lastRefill, now).TotalSeconds;
        _lastRefill = now;
        _tokens = Math.Min(_options.MessageBurst, _tokens + (elapsed * _options.MessagesPerSecond));
        if (_tokens < 1)
        {
            return false;
        }

        _tokens -= 1;
        return true;
    }
}
