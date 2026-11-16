using System.Net.WebSockets;
using System.Text;
using Coconut.Signaling.Contracts;

namespace Coconut.Client;

/// <summary>Conexão WebSocket com o servidor de sinalização: envio serializado e laço de recepção.</summary>
public sealed class SignalingConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _receiveLoop;

    /// <summary>Mensagem recebida (chamado no laço de recepção, em ordem).</summary>
    public event Func<ServerMessage, Task>? MessageReceived;

    /// <summary>A conexão terminou (fechamento do servidor ou falha de rede).</summary>
    public event Action<WebSocketCloseStatus?>? Closed;

    public async Task ConnectAsync(Uri server, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (server.Scheme == "ws" && !server.IsLoopback)
        {
            throw new ArgumentException("Sinalização sem TLS só é permitida em loopback; use wss://.", nameof(server));
        }

        await _socket.ConnectAsync(server, cancellation);
        _receiveLoop = Task.Run(ReceiveLoopAsync, CancellationToken.None); // vida própria, encerrada por _stop
    }

    public async Task SendAsync(ClientMessage message)
    {
        var bytes = Encoding.UTF8.GetBytes(SignalingJson.Serialize(message));
        await _sendLock.WaitAsync(_stop.Token);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, _stop.Token);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_socket.State == WebSocketState.Open)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
            {
                // Já caiu.
            }
        }

        if (_receiveLoop is not null)
        {
            await _receiveLoop;
        }

        _socket.Dispose();
        _sendLock.Dispose();
        _stop.Dispose();
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var parsed = SignalingJson.DeserializeServer(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                message.SetLength(0);
                if (parsed is not null && MessageReceived is { } handler)
                {
                    await handler(parsed);
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or System.Text.Json.JsonException)
        {
            // Encerrado abaixo.
        }

        Closed?.Invoke(_socket.CloseStatus);
    }
}
