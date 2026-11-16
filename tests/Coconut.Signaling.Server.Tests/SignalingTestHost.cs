using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Coconut.Signaling.Contracts;
using Coconut.Signaling.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coconut.Signaling.Server.Tests;

/// <summary>Servidor em memória com opções próprias e captura de todos os logs.</summary>
internal sealed class SignalingTestHost : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public SignalingTestHost(Action<SignalingOptions>? configure = null)
    {
        var options = new SignalingOptions { MaxRoomsPerAddress = 1000 };
        configure?.Invoke(options);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services => services.AddSingleton(options));
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(new CapturingLoggerProvider(Logs));
            });
        });
    }

    public ConcurrentQueue<string> Logs { get; } = new();

    public async Task<TestClient> ConnectAsync()
    {
        var socket = await _factory.Server.CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        return new TestClient(socket);
    }

    /// <summary>Requisição HTTP com controle do contexto (ex.: endereço de origem, esquema).</summary>
    public Task<Microsoft.AspNetCore.Http.HttpContext> SendHttpAsync(Action<Microsoft.AspNetCore.Http.HttpContext> configure) =>
        _factory.Server.SendAsync(configure);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
    }
}

/// <summary>Cliente de teste: envia mensagens do protocolo e espera respostas com prazo.</summary>
internal sealed class TestClient(WebSocket socket) : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public WebSocketCloseStatus? CloseStatus { get; private set; }

    public string? CloseDescription { get; private set; }

    public Task SendAsync(ClientMessage message) => SendRawAsync(SignalingJson.Serialize(message));

    public Task SendRawAsync(string text) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    /// <summary>Próxima mensagem; nulo quando o servidor fecha.</summary>
    public async Task<ServerMessage?> ReceiveAsync()
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var buffer = new byte[256 * 1024];
        var length = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(length), timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                CloseStatus = socket.CloseStatus;
                CloseDescription = socket.CloseStatusDescription;
                return null;
            }

            length += result.Count;
            if (result.EndOfMessage)
            {
                return SignalingJson.DeserializeServer(Encoding.UTF8.GetString(buffer, 0, length));
            }
        }
    }

    /// <summary>Lê até encontrar <typeparamref name="T"/> (ignorando outras mensagens).</summary>
    public async Task<T> ExpectAsync<T>(Func<T, bool>? match = null) where T : ServerMessage
    {
        while (true)
        {
            var message = await ReceiveAsync()
                ?? throw new InvalidOperationException($"Conexão fechada ({CloseStatus} {CloseDescription}) esperando {typeof(T).Name}");
            if (message is T typed && (match is null || match(typed)))
            {
                return typed;
            }
        }
    }

    /// <summary>Lê até o servidor fechar a conexão.</summary>
    public async Task<WebSocketCloseStatus?> ExpectClosedAsync()
    {
        while (await ReceiveAsync() is not null)
        {
        }

        return CloseStatus;
    }

    /// <summary>Queda abrupta, como perda de rede (sem LeaveRoom).</summary>
    public void Drop() => socket.Abort();

    public ValueTask DisposeAsync()
    {
        socket.Abort();
        socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
