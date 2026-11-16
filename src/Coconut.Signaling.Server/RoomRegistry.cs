using System.Collections.Concurrent;
using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Server;

internal sealed record SessionRef(RoomActor Room, ParticipantId Participant);

/// <summary>Salas abertas por convite e sessões por hash de token. Tudo em memória (salas são temporárias).</summary>
internal sealed class RoomRegistry(SignalingOptions options, TimeProvider clock, ILoggerFactory loggerFactory)
{
    private readonly ConcurrentDictionary<string, RoomActor> _rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SessionRef> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _roomsPerAddress = new(StringComparer.Ordinal);
    private readonly ILogger _log = loggerFactory.CreateLogger("Coconut.Signaling.Rooms");

    public int OpenRooms => _rooms.Count;

    public async Task<SessionRef?> CreateAsync(CreateRoom request, ClientConnection connection, string address)
    {
        if (ValidateName(request.DisplayName) is not { } name)
        {
            return Reject(connection, "createRoom", "InvalidDisplayName");
        }

        if (!TryReserveRoomSlot(address))
        {
            return Reject(connection, "createRoom", "RoomLimit");
        }

        RoomActor actor;
        do
        {
            actor = new RoomActor(Secrets.NewInviteCode(), address, options, clock, _log, OnRoomClosed);
        }
        while (!_rooms.TryAdd(actor.InviteCode, actor) && await DisposeAndRetry(actor));

        var id = ParticipantId.New();
        var token = Secrets.NewResumeToken();
        var session = new SessionRef(actor, id);
        _sessions[Secrets.Hash(token)] = session;
        await actor.CreateAsync(id, name, Secrets.Hash(token), connection, token);
        return session;
    }

    public async Task<SessionRef?> JoinAsync(JoinRoom request, ClientConnection connection)
    {
        if (ValidateName(request.DisplayName) is not { } name)
        {
            return Reject(connection, "joinRoom", "InvalidDisplayName");
        }

        // Formato inválido e convite inexistente recebem a mesma resposta.
        if (Secrets.NormalizeInvite(request.InviteCode) is not { } code || !_rooms.TryGetValue(code, out var actor))
        {
            connection.TrySend(new InviteInvalid());
            connection.Close();
            return null;
        }

        var id = ParticipantId.New();
        var token = Secrets.NewResumeToken();
        var hash = Secrets.Hash(token);
        var session = new SessionRef(actor, id);
        _sessions[hash] = session;
        if (await actor.JoinAsync(id, name, hash, connection, token))
        {
            return session;
        }

        _sessions.TryRemove(hash, out _);
        connection.Close();
        return null;
    }

    public async Task<SessionRef?> ResumeAsync(ResumeSession request, ClientConnection connection)
    {
        if (string.IsNullOrEmpty(request.ResumeToken) || request.ResumeToken.Length > 128
            || !_sessions.TryGetValue(Secrets.Hash(request.ResumeToken), out var session))
        {
            connection.TrySend(new SessionInvalid());
            connection.Close();
            return null;
        }

        return await session.Room.ResumeAsync(session.Participant, connection, request.ResumeToken) ? session : null;
    }

    private string? ValidateName(string? raw)
    {
        var name = raw?.Trim();
        return string.IsNullOrEmpty(name) || name.Length > options.MaxDisplayNameLength || name.Any(char.IsControl)
            ? null
            : name;
    }

    private static SessionRef? Reject(ClientConnection connection, string command, string reason)
    {
        connection.TrySend(new CommandRejected(command, reason));
        connection.Close();
        return null;
    }

    private bool TryReserveRoomSlot(string address)
    {
        while (true)
        {
            var current = _roomsPerAddress.GetValueOrDefault(address);
            if (current >= options.MaxRoomsPerAddress)
            {
                return false;
            }

            if (current == 0 ? _roomsPerAddress.TryAdd(address, 1) : _roomsPerAddress.TryUpdate(address, current + 1, current))
            {
                return true;
            }
        }
    }

    private void OnRoomClosed(RoomActor actor)
    {
        _rooms.TryRemove(actor.InviteCode, out _);
        foreach (var hash in actor.TokenHashes.ToList())
        {
            _sessions.TryRemove(hash, out _);
        }

        _roomsPerAddress.AddOrUpdate(actor.CreatorAddress, 0, (_, n) => Math.Max(0, n - 1));
        _ = actor.DisposeAsync().AsTask();
    }

    private static async Task<bool> DisposeAndRetry(RoomActor collided)
    {
        await collided.DisposeAsync();
        return true;
    }
}
