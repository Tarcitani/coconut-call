using Coconut.Signaling.Contracts;

namespace Coconut.Signaling.Core.Tests;

/// <summary>
/// Sequências aleatórias reprodutíveis (semente fixa) de comandos e avanços de relógio,
/// verificando os invariantes da sala após cada passo.
/// </summary>
public class RoomInvariantTests
{
    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 300; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Invariants_hold_under_random_command_sequences(int seed)
    {
        var model = new RandomRoomDriver(seed);
        for (var step = 0; step < 250 && !model.Room.IsClosed; step++)
        {
            model.Step();
        }
    }

    [Fact]
    public void Random_sequences_exercise_the_interesting_paths()
    {
        var seen = new Dictionary<string, int>();
        for (var seed = 1; seed <= 300; seed++)
        {
            var model = new RandomRoomDriver(seed);
            for (var step = 0; step < 250 && !model.Room.IsClosed; step++)
            {
                model.Step();
            }

            foreach (var (kind, count) in model.Seen)
            {
                seen[kind] = seen.GetValueOrDefault(kind) + count;
            }
        }

        foreach (var kind in new[] { "admitted", "admission-failed", "timed-out", "kicked", "share-granted", "share-requested", "handover", "room-ended" })
        {
            Assert.True(seen.GetValueOrDefault(kind) > 0, $"caminho não exercitado: {kind}; vistos: {string.Join(", ", seen.Select(s => $"{s.Key}={s.Value}"))}");
        }
    }

    private sealed class RandomRoomDriver
    {
        public Dictionary<string, int> Seen { get; } = [];

        private readonly Random _random;
        private readonly ManualClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        private readonly RoomOptions _options = new() { Capacity = 6 };
        private readonly List<ParticipantId> _known = [];
        private readonly Dictionary<(ParticipantId, ParticipantId), LinkStatus> _reports = [];
        private ShareRequestId? _lastRequest;
        private long _lastEpoch;
        private int _admissionsCheckedUpTo;

        public RandomRoomDriver(int seed)
        {
            _random = new Random(seed);
            var creator = ParticipantId.New();
            _known.Add(creator);
            var outbox = new Outbox();
            Room = Room.Create(creator, "c", _options, _clock, outbox);
            Check(new HashSet<ParticipantId> { creator }, [creator], outbox);
        }

        public Room Room { get; }

        public void Step()
        {
            var before = Room.Members.ToHashSet();
            var outbox = new Outbox();
            var joiner = Act(outbox);
            var involved = before.Concat(Room.Members).ToHashSet();
            if (joiner is { } j)
            {
                involved.Add(j);
            }

            Check(involved, Room.Members, outbox);
        }

        private ParticipantId? Act(Outbox o)
        {
            var actor = Pick();
            var other = Pick();
            switch (_random.Next(17))
            {
                case 0:
                case 1:
                    var id = ParticipantId.New();
                    _known.Add(id);
                    Room.Join(id, "p", o);
                    return id;
                case 2:
                case 3:
                case 4:
                case 5:
                    // Par que conclui a negociação: as duas pontas reportam (às vezes uma falha).
                    Report(actor, other, LinkStatus.Verified, o);
                    Report(other, actor, _random.Next(10) == 0 ? LinkStatus.Failed : LinkStatus.Verified, o);
                    break;
                case 6:
                    Report(actor, other, _random.Next(4) == 0 ? LinkStatus.Failed : LinkStatus.Verified, o);
                    break;
                case 7:
                    Room.Leave(actor, o);
                    break;
                case 8:
                    Room.SignalingLost(actor, o);
                    break;
                case 9:
                    Room.Resume(actor, o);
                    break;
                case 10:
                    if (_random.Next(2) == 0)
                    {
                        Room.Kick(_random.Next(4) == 0 ? actor : Room.Admin ?? actor, other, o);
                    }

                    break;
                case 11:
                case 12:
                    Room.RequestShare(actor, o);
                    break;
                case 13:
                    if (_lastRequest is { } r)
                    {
                        Room.RespondToShareRequest(Room.Share.Sharer ?? actor, r, _random.Next(3) != 0, o);
                    }

                    break;
                case 14:
                    if (_random.Next(2) == 0 && _lastRequest is { } c)
                    {
                        Room.CancelShareRequest(actor, c, o);
                    }
                    else if (Room.Share is { Sharer: { } s, Session: { } session })
                    {
                        Room.ShareStopped(s, session, o);
                    }

                    break;
                case 15:
                    _clock.Advance(TimeSpan.FromMilliseconds(_random.Next(0, 15_000)));
                    Room.Tick(o);
                    break;
                default:
                    if (_random.Next(40) == 0)
                    {
                        Room.EndRoom(Room.Admin ?? actor, o);
                    }

                    break;
            }

            return null;
        }

        private void Report(ParticipantId reporter, ParticipantId remote, LinkStatus status, Outbox o)
        {
            if (Room.ReportLink(reporter, remote, status, o) == CommandResult.Ok)
            {
                _reports[(reporter, remote)] = status;
            }

            // Admissões causadas por este relatório são checadas agora: um relatório seguinte
            // no mesmo passo pode legitimamente mudar o enlace depois da admissão.
            CheckAdmissions(o.Items.Skip(_admissionsCheckedUpTo));
            _admissionsCheckedUpTo = o.Items.Count;
        }

        private void CheckAdmissions(IEnumerable<Outbound> items)
        {
            foreach (var admitted in items.Select(i => i.Message).OfType<PeerAdmitted>().Where(p => p.Roster.Count > 1))
            {
                foreach (var peer in admitted.Roster.Where(p => p.Id != admitted.Admitted))
                {
                    Assert.True(Verified(admitted.Admitted, peer.Id), $"admitido sem enlace verificado com {peer.Id}");
                }
            }
        }

        /// <summary>Na maioria das vezes um membro atual; às vezes qualquer identidade já vista (inclusive removida).</summary>
        private ParticipantId Pick()
        {
            var members = Room.Members;
            return members.Count > 0 && _random.Next(10) < 8
                ? members[_random.Next(members.Count)]
                : _known[_random.Next(_known.Count)];
        }

        private void Check(IReadOnlySet<ParticipantId> involved, IReadOnlyList<ParticipantId> members, Outbox outbox)
        {
            var mesh = members.Where(m => Room.StateOf(m) is ParticipantState.Active or ParticipantState.Reconnecting).ToList();

            // Admissão só com enlace verificado nos dois sentidos com toda a malha anunciada.
            CheckAdmissions(outbox.Items.Skip(_admissionsCheckedUpTo));
            _admissionsCheckedUpTo = 0;

            Assert.True(Room.MemberCount <= _options.Capacity);
            Assert.True(Room.Epoch >= _lastEpoch, "época regrediu");
            _lastEpoch = Room.Epoch;

            if (Room.IsClosed)
            {
                Assert.Equal(0, Room.MemberCount);
                Assert.Null(Room.Share.Sharer);
            }
            else
            {
                Assert.NotEmpty(mesh);
                Assert.NotNull(Room.Admin);
                Assert.Contains(Room.Admin.Value, mesh);
            }

            if (Room.Share.Sharer is { } sharer)
            {
                Assert.Contains(sharer, mesh);
                Assert.NotNull(Room.Share.Session);
            }

            foreach (var item in outbox.Items)
            {
                Assert.Contains(item.To, involved);

                if (item.Message is ShareRequested requested)
                {
                    _lastRequest = requested.Request;
                }

                var kind = item.Message switch
                {
                    PeerAdmitted p when p.Roster.Count > 1 => "admitted",
                    AdmissionFailed => "admission-failed",
                    PeerLeft { Reason: LeaveReason.TimedOut } => "timed-out",
                    Kicked => "kicked",
                    ShareStateChanged { Sharer: not null } => "share-granted",
                    StopSharing => "handover",
                    ShareRequested => "share-requested",
                    RoomEnded => "room-ended",
                    _ => null,
                };
                if (kind is not null)
                {
                    Seen[kind] = Seen.GetValueOrDefault(kind) + 1;
                }

            }
        }

        private bool Verified(ParticipantId a, ParticipantId b) =>
            _reports.GetValueOrDefault((a, b), LinkStatus.Failed) == LinkStatus.Verified &&
            _reports.GetValueOrDefault((b, a), LinkStatus.Failed) == LinkStatus.Verified;
    }
}
