using System.Collections.Concurrent;
using System.Diagnostics;
using Coconut.Client;
using Coconut.Media.Native;
using Coconut.Signaling.Contracts;

// Ferramenta de teste: participante sem dispositivos de áudio. Entra numa sala e relata eventos.
// Uso: Tools.CallBot <ws://servidor/ws> <convite> [nome] [segundos] [--audio]
//   --audio: envia um tom de 440 Hz (como se falasse) e mede o áudio recebido de cada par (pacotes,
//   quadros decodificados e nível). Nada é reproduzido nem gravado.

var withAudio = args.Contains("--audio");
var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
if (positional.Length < 2)
{
    Console.Error.WriteLine("Uso: Tools.CallBot <ws://servidor/ws> <convite> [nome] [segundos] [--audio]");
    return 2;
}

var name = positional.Length > 2 ? positional[2] : "Bot de teste";
var seconds = positional.Length > 3 ? int.Parse(positional[3], System.Globalization.CultureInfo.InvariantCulture) : 30;
var ended = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var received = new ConcurrentDictionary<ParticipantId, PeerAudio>();
using var stop = new CancellationTokenSource();

await using var session = new MeshSession(new RtcPeerOptions());
session.Admitted += () =>
{
    Console.WriteLine($"admitido; participantes: {session.Roster.Count}");
    admitted.TrySetResult();
};
session.LinkVerified += (_, after) => Console.WriteLine($"enlace verificado em {after.TotalMilliseconds:0} ms");
session.Ended += reason => ended.TrySetResult(reason);
session.AdmissionRejected += failure => Console.WriteLine($"admissão recusada: {failure.Reason}");
if (withAudio)
{
    session.AudioReceived += (from, packet) => received.GetOrAdd(from, _ => new PeerAudio()).OnPacket(packet);
}

await session.JoinRoomAsync(new Uri(positional[0]), positional[1], name, CancellationToken.None);
var tone = withAudio
    ? Task.Run(async () =>
    {
        await admitted.Task.WaitAsync(stop.Token);
        return SendTone(session, stop.Token);
    })
    : Task.FromResult(0L);

var progress = withAudio
    ? Task.Run(async () =>
    {
        var clock = Stopwatch.StartNew();
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stop.Token).ContinueWith(_ => { }, TaskScheduler.Default);
            var totals = string.Join(", ", received.Values.Select(a => a.Packets));
            Console.WriteLine($"t={clock.Elapsed.TotalSeconds:0}s pacotes recebidos (acumulado por par): {(totals.Length == 0 ? "0" : totals)}");
        }
    })
    : Task.CompletedTask;

var finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(seconds)));
await stop.CancelAsync();
await progress;
var sent = await tone.ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : 0, TaskScheduler.Default);
if (withAudio)
{
    Console.WriteLine($"áudio enviado: {sent} quadros entregues a pelo menos um par");
    var names = session.Roster.ToDictionary(p => p.Id, p => p.DisplayName);
    if (received.IsEmpty)
    {
        Console.WriteLine("áudio recebido: NENHUM pacote");
    }

    foreach (var (peer, audio) in received)
    {
        Console.WriteLine($"áudio recebido de {names.GetValueOrDefault(peer, "?")}: {audio.Summary()}");
    }
}

if (finished == ended.Task)
{
    Console.WriteLine($"fim: {ended.Task.Result}");
}
else
{
    Console.WriteLine("tempo esgotado; saindo");
    await session.LeaveAsync();
}

foreach (var audio in received.Values)
{
    audio.Dispose();
}

return 0;

// Tom de 440 Hz em quadros de 20 ms no ritmo real, codificado uma vez e enviado à malha.
static long SendTone(MeshSession session, CancellationToken stop)
{
    using var encoder = new OpusEncoder();
    var pcm = new short[OpusCodec.FrameSamples];
    var packet = new byte[1500];
    var clock = Stopwatch.StartNew();
    var timestamp = (uint)Random.Shared.NextInt64(uint.MaxValue);
    long delivered = 0;
    for (long frame = 0; !stop.IsCancellationRequested; frame++)
    {
        for (var i = 0; i < pcm.Length; i++)
        {
            var t = (double)((frame * pcm.Length) + i) / OpusCodec.SampleRate;
            pcm[i] = (short)(Math.Sin(2 * Math.PI * 440 * t) * 1500); // RMS ~1060: baixo, mas acima do limiar de fala (300)
        }

        var length = encoder.Encode(pcm, packet);
        if (session.SendAudio(packet.AsSpan(0, length), timestamp) > 0)
        {
            delivered++;
        }

        timestamp += OpusCodec.FrameSamples;
        var wait = TimeSpan.FromMilliseconds((frame + 1) * 20) - clock.Elapsed;
        if (wait > TimeSpan.Zero)
        {
            Thread.Sleep(wait);
        }
    }

    return delivered;
}

internal sealed class PeerAudio : IDisposable
{
    private readonly Lock _gate = new();
    private readonly OpusDecoder _decoder = new();
    private readonly short[] _pcm = new short[OpusCodec.MaxFrameSamples];
    private long _packets;
    private long _decoded;
    private double _maxRms;
    private bool _disposed;

    public long Packets => Interlocked.Read(ref _packets);

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
                return;
            }

            _packets++;
            var samples = _decoder.Decode(packet.Slice(rtp.PayloadOffset, rtp.PayloadLength), _pcm);
            if (samples > 0)
            {
                _decoded++;
                double sum = 0;
                for (var i = 0; i < samples; i++)
                {
                    sum += (double)_pcm[i] * _pcm[i];
                }

                _maxRms = Math.Max(_maxRms, Math.Sqrt(sum / samples));
            }
        }
    }

    public string Summary()
    {
        lock (_gate)
        {
            return $"{_packets} pacotes, {_decoded} decodificados, nível máx. {_maxRms:0}";
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
