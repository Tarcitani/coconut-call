using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core;

public sealed record Outbound(ParticipantId To, ServerMessage Message);

/// <summary>
/// Coleta as mensagens produzidas por um comando. O host as entrega depois que o comando termina,
/// o que mantém o núcleo livre de E/S e determinístico.
/// </summary>
public sealed class Outbox
{
    private readonly List<Outbound> _items = [];

    public IReadOnlyList<Outbound> Items => _items;

    public void Send(ParticipantId to, ServerMessage message) => _items.Add(new Outbound(to, message));
}
