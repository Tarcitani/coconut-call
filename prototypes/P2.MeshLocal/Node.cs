using System.Collections.Concurrent;
using System.Diagnostics;
using Coconut.Client;
using Coconut.Media.Native;
using Coconut.Signaling.Contracts;

namespace Coconut.Prototypes.P2;

internal sealed record PeerAudioStats(int RtpPackets, int DecodedFrames);

internal sealed record NodeResult(
    int Index,
    bool Admitted,
    double? AdmittedAfterMs,
    long Epoch,
    int RosterSize,
    int LinksVerified,
    double? LinkVerifyMedianMs,
    double? LinkVerifyMaxMs,
    int FramesSent,
    IReadOnlyDictionary<string, PeerAudioStats> AudioByPeer,
    IReadOnlyDictionary<string, int> SelectedCandidateTypes,
    double CpuSeconds,
    double PeakWorkingSetMb,
    string? EndReason,
    IReadOnlyDictionary<string, int> MessagesByType,
    IReadOnlyList<string> Diagnostics);

/// <summary>Um participante: cria ou entra na sala, forma a malha e troca áudio Opus com todos.</summary>
internal static class Node
{
    public const int AudioFrames = 50; // 1 s

    public static async Task<NodeResult> RunAsync(int index, string runDir, Uri server)
    {
        var started = Stopwatch.StartNew();
        var diagnostics = new ConcurrentQueue<string>();
        var verifyTimes = new ConcurrentBag<double>();
        var audio = new ConcurrentDictionary<ParticipantId, Receiver>();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageTypes = new ConcurrentDictionary<string, int>();
        double? admittedAfter = null;
        string? endReason = null;

        await using var session = new MeshSession(new RtcPeerOptions());
        session.Diagnostic += d => diagnostics.Enqueue(d);
        session.MessageHandled += m =>
        {
            messageTypes.AddOrUpdate(m.GetType().Name, 1, (_, n) => n + 1);
            if (m is AdmissionFailed failed)
            {
                diagnostics.Enqueue($"admissão recusada: {failed.Reason}; pares: {string.Join(", ", failed.FailedPeers.Select(p => p.State))}");
            }
        };
        session.LinkVerified += (_, after) => verifyTimes.Add(after.TotalMilliseconds);
        session.Admitted += () =>
        {
            admittedAfter = started.Elapsed.TotalMilliseconds;
            admitted.TrySetResult();
        };
        session.Ended += reason =>
        {
            endReason ??= reason;
            admitted.TrySetResult();
        };
        session.AudioReceived += (from, packet) => audio.GetOrAdd(from, _ => new Receiver()).OnPacket(packet);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (index == 0)
        {
            await session.CreateRoomAsync(server, "n0", timeout.Token);
            while (session.InviteCode is null)
            {
                await Task.Delay(20, timeout.Token);
            }

            Exchange.Write(runDir, "invite.txt", session.InviteCode);
        }
        else
        {
            var invite = await Exchange.WaitForAsync(runDir, "invite.txt", timeout.Token);
            await session.JoinRoomAsync(server, invite, $"n{index}", timeout.Token);
        }

        await admitted.Task.WaitAsync(timeout.Token);
        Exchange.Write(runDir, $"admitted-{index}", "1");

        // O coordenador libera o áudio quando todos foram admitidos (malha completa).
        await Exchange.WaitForAsync(runDir, "audio.go", timeout.Token);
        var sent = SendTone(session, index);
        Exchange.Write(runDir, $"audio-done-{index}", "1");
        await Exchange.WaitForAsync(runDir, "stop", timeout.Token);

        var candidates = (await session.GetSelectedCandidateTypesAsync()).Values
            .SelectMany(p => new[] { p.Local, p.Remote })
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());
        var times = verifyTimes.Order().ToList();
        var process = Process.GetCurrentProcess();
        var result = new NodeResult(
            index,
            session.IsAdmitted,
            admittedAfter,
            session.Epoch,
            session.Roster.Count,
            times.Count,
            times.Count > 0 ? times[times.Count / 2] : null,
            times.Count > 0 ? times[^1] : null,
            sent,
            audio.ToDictionary(a => a.Key.ToString(), a => a.Value.Stats()),
            candidates,
            process.TotalProcessorTime.TotalSeconds,
            process.PeakWorkingSet64 / (1024.0 * 1024.0),
            endReason,
            messageTypes.ToDictionary(),
            diagnostics.ToList());

        // Barreira: ninguém sai antes de todos registrarem o estado (senão a época muda durante o retrato).
        Exchange.Write(runDir, $"reported-{index}", "1");
        await Exchange.WaitForAsync(runDir, "leave", timeout.Token);
        await session.LeaveAsync();
        foreach (var receiver in audio.Values)
        {
            receiver.Dispose();
        }

        return result;
    }

    /// <summary>Codifica cada quadro uma vez e entrega o mesmo pacote a todos os pares (malha).</summary>
    private static int SendTone(MeshSession session, int index)
    {
        using var encoder = new OpusEncoder();
        var pcm = new short[OpusCodec.FrameSamples];
        var packet = new byte[1500];
        var frequency = 300.0 + (40 * index);
        var clock = Stopwatch.StartNew();
        var timestamp = (uint)Random.Shared.NextInt64(uint.MaxValue);
        var framesSent = 0;
        for (var frame = 0; frame < AudioFrames; frame++)
        {
            for (var i = 0; i < pcm.Length; i++)
            {
                var t = (double)((frame * pcm.Length) + i) / OpusCodec.SampleRate;
                pcm[i] = (short)(Math.Sin(2 * Math.PI * frequency * t) * 6000);
            }

            var length = encoder.Encode(pcm, packet);
            if (session.SendAudio(packet.AsSpan(0, length), timestamp) > 0)
            {
                framesSent++;
            }

            timestamp += OpusCodec.FrameSamples;
            var wait = TimeSpan.FromMilliseconds((frame + 1) * 20) - clock.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                Thread.Sleep(wait);
            }
        }

        return framesSent;
    }

    private sealed class Receiver : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly OpusDecoder _decoder = new();
        private readonly short[] _pcm = new short[OpusCodec.MaxFrameSamples];
        private int _packets;
        private int _decoded;
        private bool _disposed;

        public void OnPacket(ReadOnlySpan<byte> packet)
        {
            if (!RtpPacket.TryParse(packet, out var rtp) || rtp.PayloadType != RtcPeer.OpusPayloadType)
            {
                return;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    return; // pacote tardio após o fim da sessão
                }

                _packets++;
                if (_decoder.Decode(packet.Slice(rtp.PayloadOffset, rtp.PayloadLength), _pcm) > 0)
                {
                    _decoded++;
                }
            }
        }

        public PeerAudioStats Stats()
        {
            lock (_gate)
            {
                return new PeerAudioStats(_packets, _decoded);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _decoder.Dispose();
            }
        }
    }
}

internal static class Exchange
{
    public static void Write(string dir, string name, string content)
    {
        var temp = Path.Combine(dir, name + "." + Environment.ProcessId + ".tmp");
        File.WriteAllText(temp, content);
        File.Move(temp, Path.Combine(dir, name), overwrite: true);
    }

    public static async Task<string> WaitForAsync(string dir, string name, CancellationToken cancellation)
    {
        var path = Path.Combine(dir, name);
        while (!File.Exists(path))
        {
            await Task.Delay(25, cancellation);
        }

        return await File.ReadAllTextAsync(path, cancellation);
    }
}
