using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core.Tests;

internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}

internal sealed record Step(CommandResult Result, Outbox Outbox)
{
    public IEnumerable<T> To<T>(ParticipantId id) where T : ServerMessage =>
        Outbox.Items.Where(o => o.To == id).Select(o => o.Message).OfType<T>();

    public IEnumerable<ParticipantId> RecipientsOf<T>() where T : ServerMessage =>
        Outbox.Items.Where(o => o.Message is T).Select(o => o.To);
}

/// <summary>Sala com relógio manual e atalhos para os fluxos comuns.</summary>
internal sealed class RoomHarness
{
    private readonly List<ParticipantId> _known = [];

    public RoomHarness(RoomOptions? options = null)
    {
        Options = options ?? new RoomOptions();
        var outbox = new Outbox();
        Room = Room.Create(Creator, "criador", Options, Clock, outbox);
        Created = new Step(CommandResult.Ok, outbox);
        _known.Add(Creator);
    }

    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public RoomOptions Options { get; }

    public Room Room { get; }

    public ParticipantId Creator { get; } = ParticipantId.New();

    public Step Created { get; }

    public IEnumerable<ParticipantId> Mesh =>
        _known.Where(id => Room.StateOf(id) is ParticipantState.Active or ParticipantState.Reconnecting);

#pragma warning disable CA1822 // Chamado via instância (h.Run) para manter os testes legíveis.
    public Step Run(Func<Outbox, CommandResult> command)
    {
        var outbox = new Outbox();
        return new Step(command(outbox), outbox);
    }

    public Step Run(Action<Outbox> command)
    {
        var outbox = new Outbox();
        command(outbox);
        return new Step(CommandResult.Ok, outbox);
    }
#pragma warning restore CA1822

    public ParticipantId Join(string name = "p")
    {
        var id = ParticipantId.New();
        var step = Run(o => Room.Join(id, name, o));
        Assert.Equal(CommandResult.Ok, step.Result);
        _known.Add(id);
        return id;
    }

    public Step Report(ParticipantId reporter, ParticipantId remote, LinkStatus status = LinkStatus.Verified) =>
        Run(o => Room.ReportLink(reporter, remote, status, o));

    public CommandResult Relay(ParticipantId from, ParticipantId to) => Run(o => Room.CanRelay(from, to, o)).Result;

    /// <summary>As duas extremidades reportam o enlace como verificado.</summary>
    public Step Link(ParticipantId a, ParticipantId b)
    {
        Report(a, b);
        return Report(b, a);
    }

    /// <summary>Entra e verifica enlaces com toda a malha atual.</summary>
    public ParticipantId JoinAndAdmit(string name = "p")
    {
        var mesh = Mesh.ToList();
        var id = Join(name);
        foreach (var peer in mesh)
        {
            Link(id, peer);
        }

        Assert.Equal(ParticipantState.Active, Room.StateOf(id));
        return id;
    }

    public Step Tick(TimeSpan advance)
    {
        Clock.Advance(advance);
        return Run(o => Room.Tick(o));
    }
}
