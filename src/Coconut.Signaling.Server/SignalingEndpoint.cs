using System.Net.WebSockets;
using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Server;

/// <summary>Ciclo de vida de uma conexão: handshake (criar/entrar/retomar) e depois comandos da sala.</summary>
internal static class SignalingEndpoint
{
    public static async Task HandleAsync(HttpContext context, RoomRegistry registry, SignalingOptions options, TimeProvider clock)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var aborted = context.RequestAborted;
        var connection = new ClientConnection(socket, options, clock);
        var writer = connection.RunWriterAsync(aborted);

        // Endereço usado só em memória para limitar salas por origem; nunca registrado.
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var session = await OpenSessionAsync(connection, registry, options, address, aborted);
        if (session is null)
        {
            connection.Close(WebSocketCloseStatus.PolicyViolation, "no-session");
            await writer;
            return;
        }

        try
        {
            while (await connection.ReceiveAsync(aborted) is { } message)
            {
                await session.Room.HandleAsync(session.Participant, message, connection);
            }
        }
        finally
        {
            await session.Room.ConnectionLostAsync(session.Participant, connection);
            connection.Close();
            await writer;
        }
    }

    private static async Task<SessionRef?> OpenSessionAsync(
        ClientConnection connection, RoomRegistry registry, SignalingOptions options, string address, CancellationToken aborted)
    {
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        handshake.CancelAfter(options.HandshakeTimeout);

        return await connection.ReceiveAsync(handshake.Token) switch
        {
            CreateRoom create => await registry.CreateAsync(create, connection, address),
            JoinRoom join => await registry.JoinAsync(join, connection),
            ResumeSession resume => await registry.ResumeAsync(resume, connection),
            _ => null, // prazo esgotado, conexão encerrada ou primeira mensagem fora do protocolo
        };
    }
}
