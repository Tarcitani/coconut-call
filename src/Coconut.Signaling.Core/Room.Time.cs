namespace Coconut.Signaling.Core;

/// <summary>Aplicação de prazos em ordem cronológica.</summary>
public sealed partial class Room
{
    // Desempate para prazos simultâneos: a remoção de quem não voltou acontece antes,
    // pois pode liberar uma admissão; depois, o compartilhamento; por último, a expiração de admissões.
    private const int ReconnectExpiryPriority = 0;
    private const int ShareExpiryPriority = 1;
    private const int AdmissionExpiryPriority = 2;

    /// <summary>
    /// Aplica, um por vez e do mais antigo ao mais recente, todos os prazos vencidos até agora,
    /// reavaliando admissões após cada um. Equivale a ter recebido um Tick no instante exato de cada prazo.
    /// </summary>
    private void AdvanceTime(Outbox outbox)
    {
        var now = _clock.GetUtcNow();
        while (!IsClosed && NextDue(now) is { } due)
        {
            due.Apply(outbox);
            EvaluateAdmissions(outbox);
        }
    }

    private DueEvent? NextDue(DateTimeOffset now)
    {
        DueEvent? best = null;

        foreach (var member in _members.Values)
        {
            var candidate = member.State switch
            {
                Contracts.ParticipantState.Reconnecting when member.Deadline <= now =>
                    new DueEvent(member.Deadline, ReconnectExpiryPriority, member.JoinSequence, o => Remove(member.Id, Contracts.LeaveReason.TimedOut, o)),
                Contracts.ParticipantState.Pending when member.Deadline <= now =>
                    new DueEvent(member.Deadline, AdmissionExpiryPriority, member.JoinSequence, o => ExpireAdmission(member, o)),
                _ => null,
            };
            best = Earlier(best, candidate);
        }

        if (Share.NextDeadline is { } shareDeadline && shareDeadline <= now)
        {
            best = Earlier(best, new DueEvent(shareDeadline, ShareExpiryPriority, 0, Share.ExpireDue));
        }

        return best;
    }

    private void ExpireAdmission(Member pending, Outbox outbox)
    {
        var missing = Mesh().Where(r => !_links.IsVerified(pending.Id, r.Id)).Select(r => r.ToInfo()).ToList();
        FailAdmission(pending, Contracts.AdmissionFailure.Timeout, missing, outbox);
    }

    private static DueEvent? Earlier(DueEvent? a, DueEvent? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return (b.At, b.Priority, b.Order).CompareTo((a.At, a.Priority, a.Order)) < 0 ? b : a;
    }

    private sealed record DueEvent(DateTimeOffset At, int Priority, long Order, Action<Outbox> Apply);
}
