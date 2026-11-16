using System.Threading.Channels;
using Coconut.Signaling.Contracts;
using Coconut.Signaling.Core;

namespace Coconut.Signaling.Server;

/// <summary>
/// Dono exclusivo de um <see cref="Room"/>: todo acesso passa pela fila e é executado em série
/// (ADR 0003). Depois de cada comando entrega o <see cref="Outbox"/> e fecha conexões de quem
/// deixou de ser membro.
/// </summary>
internal sealed partial class RoomActor : IAsyncDisposable
{
    private readonly Channel<Action> _mailbox = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<ParticipantId, ClientConnection> _connections = [];
    private readonly HashSet<string> _tokenHashes = [];
    private readonly SignalingOptions _options;
    private readonly ILogger _log;
    private readonly Action<RoomActor> _onClosed;
    private readonly ITimer _ticker;
    private readonly Task _loop;
    private Room? _room;

    public RoomActor(string inviteCode, string creatorAddress, SignalingOptions options, TimeProvider clock, ILogger log, Action<RoomActor> onClosed)
    {
        InviteCode = inviteCode;
        CreatorAddress = creatorAddress;
        Clock = clock;
        _options = options;
        _log = log;
        _onClosed = onClosed;
        _loop = Task.Run(RunAsync);
        _ticker = clock.CreateTimer(_ => Post(Tick), null, options.TickInterval, options.TickInterval);
    }

    public string InviteCode { get; }

    /// <summary>Usado só para o limite de salas por endereço; nunca registrado.</summary>
    public string CreatorAddress { get; }

    public string LogHandle { get; } = Secrets.NewLogHandle();

    public TimeProvider Clock { get; }

    public IReadOnlyCollection<string> TokenHashes => _tokenHashes;

    public Task<bool> CreateAsync(ParticipantId creator, string displayName, string tokenHash, ClientConnection connection, string resumeToken) =>
        InvokeAsync(outbox =>
        {
            _connections[creator] = connection;
            _tokenHashes.Add(tokenHash);
            connection.TrySend(new SessionEstablished(creator, resumeToken, InviteCode));
            _room = Room.Create(creator, displayName, _options.Room, Clock, outbox);
            LogRoomCreated(_log, LogHandle);
            return true;
        });

    public Task<bool> JoinAsync(ParticipantId id, string displayName, string tokenHash, ClientConnection connection, string resumeToken) =>
        InvokeAsync(outbox =>
        {
            if (_room is null || _room.IsClosed)
            {
                connection.TrySend(new InviteInvalid());
                return false;
            }

            _connections[id] = connection;
            connection.TrySend(new SessionEstablished(id, resumeToken, InviteCode));
            var result = _room.Join(id, displayName, outbox);
            if (result == CommandResult.Ok)
            {
                _tokenHashes.Add(tokenHash);
                return true;
            }

            if (result != CommandResult.RoomFull)
            {
                connection.TrySend(new CommandRejected("joinRoom", result.ToString()));
            }

            return false;
        });

    public Task<bool> ResumeAsync(ParticipantId id, ClientConnection connection, string resumeToken) =>
        InvokeAsync(outbox =>
        {
            if (_room is null)
            {
                connection.TrySend(new SessionInvalid());
                return false;
            }

            var previous = _connections.GetValueOrDefault(id);
            _connections[id] = connection;
            connection.TrySend(new SessionEstablished(id, resumeToken, InviteCode));
            var result = _room.Resume(id, outbox);
            if (result != CommandResult.Ok)
            {
                _connections.Remove(id);
                connection.TrySend(new SessionInvalid());
                connection.Close();
                return false;
            }

            // A conexão antiga (se ainda aberta) é substituída sem gerar perda de sinalização.
            if (previous is not null && !ReferenceEquals(previous, connection))
            {
                previous.Close();
            }

            return true;
        });

    public Task HandleAsync(ParticipantId id, ClientMessage message, ClientConnection connection) =>
        InvokeAsync(outbox =>
        {
            if (_room is null || !ReferenceEquals(_connections.GetValueOrDefault(id), connection))
            {
                connection.Close();
                return false;
            }

            var result = Execute(_room, id, message, outbox);
            if (result is not (CommandResult.Ok or CommandResult.Busy))
            {
                connection.TrySend(new CommandRejected(CommandName(message), result.ToString()));
            }

            return true;
        });

    /// <summary>A conexão terminou. Só conta como perda de sinalização se ainda for a conexão vigente.</summary>
    public Task ConnectionLostAsync(ParticipantId id, ClientConnection connection) =>
        InvokeAsync(outbox =>
        {
            if (_room is not null && ReferenceEquals(_connections.GetValueOrDefault(id), connection))
            {
                _connections.Remove(id);
                _room.SignalingLost(id, outbox);
            }

            return true;
        });

    public async ValueTask DisposeAsync()
    {
        await _ticker.DisposeAsync();
        _mailbox.Writer.TryComplete();
        await _loop;
    }

    private CommandResult Execute(Room room, ParticipantId id, ClientMessage message, Outbox outbox) => message switch
    {
        ReportLink m => room.ReportLink(id, m.Peer, m.Status, outbox),
        Relay m => RelayTo(room, id, m, outbox),
        LeaveRoom => room.Leave(id, outbox),
        KickParticipant m => room.Kick(id, m.Target, outbox),
        EndRoomForAll => room.EndRoom(id, outbox),
        RequestShare => room.RequestShare(id, outbox),
        RespondToShareRequest m => room.RespondToShareRequest(id, m.Request, m.Accept, outbox),
        CancelShareRequest m => room.CancelShareRequest(id, m.Request, outbox),
        ShareStopped m => room.ShareStopped(id, m.Session, outbox),
        _ => CommandResult.NotAllowed, // abrir sessão de novo numa conexão já estabelecida
    };

    private CommandResult RelayTo(Room room, ParticipantId from, Relay relay, Outbox outbox)
    {
        if (relay.Payload.Length > _options.MaxRelayPayloadChars)
        {
            return CommandResult.InvalidTarget;
        }

        var result = room.CanRelay(from, relay.To, outbox);
        if (result == CommandResult.Ok)
        {
            // Conteúdo opaco: nunca interpretado nem registrado (contém IPs nos candidatos).
            outbox.Send(relay.To, new Relayed(from, relay.Kind, relay.Payload));
        }

        return result;
    }

    private void Tick() => Run(outbox =>
    {
        _room?.Tick(outbox);
        return true;
    });

    private Task<bool> InvokeAsync(Func<Outbox, bool> work)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = Post(() =>
        {
            try
            {
                completion.TrySetResult(Run(work));
            }
            catch (Exception ex)
            {
                // Quem aguarda é liberado; o laço registra a falha.
                completion.TrySetException(ex);
                throw;
            }
        });

        if (!posted)
        {
            completion.TrySetResult(false); // sala já encerrada
        }

        return completion.Task;
    }

    private bool Post(Action action) => _mailbox.Writer.TryWrite(action);

    private bool Run(Func<Outbox, bool> work)
    {
        var outbox = new Outbox();
        var result = work(outbox);
        foreach (var item in outbox.Items)
        {
            if (_connections.TryGetValue(item.To, out var connection))
            {
                connection.TrySend(item.Message);
            }
        }

        PruneConnections();
        return result;
    }

    /// <summary>Quem deixou de ser membro recebeu sua última mensagem; a conexão é encerrada.</summary>
    private void PruneConnections()
    {
        if (_room is null)
        {
            return;
        }

        foreach (var id in _connections.Keys.Where(id => _room.StateOf(id) is null).ToList())
        {
            _connections[id].Close();
            _connections.Remove(id);
        }

        if (_room.IsClosed && _mailbox.Writer.TryComplete())
        {
            LogRoomClosed(_log, LogHandle);
            _onClosed(this);
        }
    }

    private async Task RunAsync()
    {
        await foreach (var action in _mailbox.Reader.ReadAllAsync())
        {
            try
            {
                action();
            }
#pragma warning disable CA1031 // Uma falha num comando não pode derrubar a sala inteira; é registrada.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogCommandFailed(_log, LogHandle, ex);
            }
        }
    }

    private static string CommandName(ClientMessage message) => message switch
    {
        ReportLink => "reportLink",
        Relay => "relay",
        LeaveRoom => "leaveRoom",
        KickParticipant => "kick",
        EndRoomForAll => "endRoom",
        RequestShare => "requestShare",
        RespondToShareRequest => "respondShare",
        CancelShareRequest => "cancelShare",
        ShareStopped => "shareStopped",
        _ => "session",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Sala {Room} criada")]
    private static partial void LogRoomCreated(ILogger logger, string room);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sala {Room} encerrada")]
    private static partial void LogRoomClosed(ILogger logger, string room);

    [LoggerMessage(Level = LogLevel.Error, Message = "Falha ao processar comando na sala {Room}")]
    private static partial void LogCommandFailed(ILogger logger, string room, Exception exception);
}
