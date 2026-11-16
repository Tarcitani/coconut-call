using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core;

/// <summary>
/// Relatórios de enlace P2P por extremidade. Um enlace só é verificado quando as duas
/// extremidades reportaram <see cref="LinkStatus.Verified"/>; um único relatório de falha basta para falhar.
/// </summary>
internal sealed class LinkTable
{
    private readonly Dictionary<(ParticipantId Reporter, ParticipantId Remote), LinkStatus> _reports = [];

    public void Report(ParticipantId reporter, ParticipantId remote, LinkStatus status) =>
        _reports[(reporter, remote)] = status;

    public bool IsVerified(ParticipantId a, ParticipantId b) =>
        Is(a, b, LinkStatus.Verified) && Is(b, a, LinkStatus.Verified);

    public bool HasFailed(ParticipantId a, ParticipantId b) =>
        Is(a, b, LinkStatus.Failed) || Is(b, a, LinkStatus.Failed);

    public void Forget(ParticipantId id)
    {
        foreach (var key in _reports.Keys.Where(k => k.Reporter == id || k.Remote == id).ToList())
        {
            _reports.Remove(key);
        }
    }

    private bool Is(ParticipantId reporter, ParticipantId remote, LinkStatus status) =>
        _reports.TryGetValue((reporter, remote), out var s) && s == status;
}
