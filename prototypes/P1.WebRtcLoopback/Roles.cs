using System.Runtime.InteropServices;
using Coconut.Prototypes.P1.Native;

namespace Coconut.Prototypes.P1;

internal sealed record OffererResult(
    bool Connected,
    double? ConnectedMs,
    int PingsSent,
    int PongsReceived,
    double? RttMedianMs,
    double? RttMaxMs,
    int AudioFramesSent,
    string SelectedLocalType,
    string SelectedRemoteType,
    SdpEvidence Offer,
    SdpEvidence Answer,
    string? AnswererReport,
    IReadOnlyList<string> Errors);

internal sealed record AnswererResult(
    bool Connected,
    int RtpPackets,
    int DecodedFrames,
    int SequenceGaps,
    double TimestampStepOkRatio,
    double ToneToOffBandRatio,
    double Rms,
    IReadOnlyList<string> Errors);

internal static unsafe class Roles
{
    public const int Pings = 20;
    public const int AudioFrames = 150; // 3 s
    private const byte OpusPayloadType = 111;
    private const uint AudioSsrc = 42;
    private const uint InitialTimestamp = 1000;
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(15);

    public static OffererResult RunOfferer(string dir)
    {
        PeerSession.Create();
        var pc = PeerSession.PeerConnection;

        var dc = RtcNative.CreateDataChannel(pc, "probe");
        RtcNative.Check(dc, "rtcCreateDataChannel");
        PeerSession.UseDataChannel(dc);

        var track = AddOpusSendTrack(pc);
        PeerSession.UseTrack(track);

        RtcNative.Check(RtcNative.SetLocalDescription(pc, "offer"), "rtcSetLocalDescription(offer)");
        Require(PeerSession.GatheringComplete.Wait(Step), "coleta de candidatos");
        var offer = PeerSession.LocalDescription();
        Exchange.Write(dir, "offer.sdp", offer);

        var answer = Exchange.WaitFor(dir, "answer.sdp", TimeSpan.FromSeconds(30));
        RtcNative.Check(RtcNative.SetRemoteDescription(pc, answer, "answer"), "rtcSetRemoteDescription(answer)");

        var connected = PeerSession.Connected.Wait(Step);
        Require(connected && PeerSession.DataChannelOpen.Wait(Step), "conexão e canal de dados");

        var rtts = new List<double>();
        for (var i = 0; i < Pings; i++)
        {
            var sentAt = PeerSession.Clock.Elapsed.TotalMilliseconds;
            PeerSession.SendText($"ping:{i}");
            if (PeerSession.WaitChannelMessage(TimeSpan.FromSeconds(2)) == $"pong:{i}")
            {
                rtts.Add(PeerSession.Clock.Elapsed.TotalMilliseconds - sentAt);
            }
        }

        Require(PeerSession.TrackOpen.Wait(Step), "trilha de áudio aberta");
        var sent = SendTone(track);

        PeerSession.SendText("done");
        string? report = null;
        var deadline = DateTime.UtcNow + Step;
        while (report is null && DateTime.UtcNow < deadline)
        {
            var message = PeerSession.WaitChannelMessage(TimeSpan.FromSeconds(1));
            if (message?.StartsWith("result:", StringComparison.Ordinal) == true)
            {
                report = message["result:".Length..];
            }
        }

        var (local, remote) = PeerSession.SelectedCandidateTypes();
        PeerSession.Close();

        rtts.Sort();
        return new OffererResult(
            connected,
            PeerSession.ConnectedAtMs,
            Pings,
            rtts.Count,
            rtts.Count > 0 ? rtts[rtts.Count / 2] : null,
            rtts.Count > 0 ? rtts[^1] : null,
            sent,
            local,
            remote,
            Sdp.Inspect(offer),
            Sdp.Inspect(answer),
            report,
            PeerSession.Errors.ToList());
    }

    public static AnswererResult RunAnswerer(string dir)
    {
        var receiver = new ToneReceiver();
        PeerSession.OnTrackPacket = receiver.OnPacket;
        PeerSession.Create();
        var pc = PeerSession.PeerConnection;

        var offer = Exchange.WaitFor(dir, "offer.sdp", TimeSpan.FromSeconds(30));
        RtcNative.Check(RtcNative.SetRemoteDescription(pc, offer, "offer"), "rtcSetRemoteDescription(offer)");
        RtcNative.Check(RtcNative.SetLocalDescription(pc, "answer"), "rtcSetLocalDescription(answer)");
        Require(PeerSession.GatheringComplete.Wait(Step), "coleta de candidatos");
        Exchange.Write(dir, "answer.sdp", PeerSession.LocalDescription());

        var connected = PeerSession.Connected.Wait(Step);
        Require(connected && PeerSession.DataChannelOpen.Wait(Step), "conexão e canal de dados");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            var message = PeerSession.WaitChannelMessage(TimeSpan.FromSeconds(1));
            if (message is null)
            {
                continue;
            }

            if (message.StartsWith("ping:", StringComparison.Ordinal))
            {
                PeerSession.SendText("pong:" + message["ping:".Length..]);
            }
            else if (message == "done")
            {
                break;
            }
        }

        // Pequena folga para pacotes ainda em trânsito.
        Thread.Sleep(300);
        var result = receiver.Result(connected, PeerSession.Errors.ToList());
        PeerSession.SendText("result:" + System.Text.Json.JsonSerializer.Serialize(result));
        Thread.Sleep(300);
        PeerSession.Close();
        receiver.Dispose();
        return result;
    }

    private static int AddOpusSendTrack(int pc)
    {
        var mid = Marshal.StringToCoTaskMemUTF8("audio");
        var msid = Marshal.StringToCoTaskMemUTF8("coconut");
        var trackId = Marshal.StringToCoTaskMemUTF8("mic");
        var cname = Marshal.StringToCoTaskMemUTF8("coconut-p1");
        try
        {
            var init = new RtcTrackInit
            {
                Direction = RtcDirection.SendOnly,
                Codec = RtcCodec.Opus,
                PayloadType = OpusPayloadType,
                Ssrc = AudioSsrc,
                Mid = (byte*)mid,
                Msid = (byte*)msid,
                TrackId = (byte*)trackId,
            };
            var track = RtcNative.AddTrackEx(pc, &init);
            RtcNative.Check(track, "rtcAddTrackEx");

            var packetizer = new RtcPacketizerInit
            {
                Ssrc = AudioSsrc,
                Cname = (byte*)cname,
                PayloadType = OpusPayloadType,
                ClockRate = Tone.SampleRate,
                SequenceNumber = 1,
                Timestamp = InitialTimestamp,
            };
            RtcNative.Check(RtcNative.SetOpusPacketizer(track, &packetizer), "rtcSetOpusPacketizer");
            RtcNative.Check(RtcNative.ChainRtcpSrReporter(track), "rtcChainRtcpSrReporter");
            return track;
        }
        finally
        {
            Marshal.FreeCoTaskMem(mid);
            Marshal.FreeCoTaskMem(msid);
            Marshal.FreeCoTaskMem(trackId);
            Marshal.FreeCoTaskMem(cname);
        }
    }

    private static int SendTone(int track)
    {
        int error;
        var encoder = OpusNative.EncoderCreate(Tone.SampleRate, 1, OpusNative.ApplicationVoip, &error);
        Require(encoder != IntPtr.Zero && error == 0, $"opus_encoder_create ({error})");
        var packet = new byte[1500];
        var start = PeerSession.Clock.Elapsed;
        var sent = 0;
        try
        {
            for (var i = 0; i < AudioFrames; i++)
            {
                var pcm = Tone.Frame(i);
                int length;
                fixed (short* p = pcm)
                fixed (byte* o = packet)
                {
                    length = OpusNative.Encode(encoder, p, Tone.FrameSamples, o, packet.Length);
                }

                Require(length > 0, $"opus_encode ({length})");
                RtcNative.Check(RtcNative.SetTrackRtpTimestamp(track, InitialTimestamp + (uint)(i * Tone.FrameSamples)), "rtcSetTrackRtpTimestamp");
                fixed (byte* o = packet)
                {
                    if (RtcNative.SendMessage(track, o, length) >= 0)
                    {
                        sent++;
                    }
                }

                // Cadência de 20 ms por quadro, ancorada no início para não acumular atraso.
                var due = start + TimeSpan.FromMilliseconds((i + 1) * 20);
                var wait = due - PeerSession.Clock.Elapsed;
                if (wait > TimeSpan.Zero)
                {
                    Thread.Sleep(wait);
                }
            }
        }
        finally
        {
            OpusNative.EncoderDestroy(encoder);
        }

        return sent;
    }

    private static void Require(bool condition, string what)
    {
        if (!condition)
        {
            throw new TimeoutException($"Falhou: {what} (estado {PeerSession.LastState}; erros: {string.Join("; ", PeerSession.Errors)})");
        }
    }
}

/// <summary>Decodifica o Opus recebido e mede se o tom de 440 Hz chegou íntegro.</summary>
internal sealed unsafe class ToneReceiver : IDisposable
{
    private readonly object _gate = new();
    private readonly List<short> _pcm = [];
    private readonly IntPtr _decoder;
    private int _packets;
    private int _decoded;
    private int _gaps;
    private int _steps;
    private int _goodSteps;
    private ushort? _lastSequence;
    private uint? _lastTimestamp;

    public ToneReceiver()
    {
        int error;
        _decoder = OpusNative.DecoderCreate(Tone.SampleRate, 1, &error);
        if (_decoder == IntPtr.Zero || error != 0)
        {
            throw new InvalidOperationException($"opus_decoder_create ({error})");
        }
    }

    public void OnPacket(ReadOnlySpan<byte> packet)
    {
        if (!RtpPacket.TryParse(packet, out var rtp) || rtp.PayloadType != 111)
        {
            return;
        }

        lock (_gate)
        {
            _packets++;
            if (_lastSequence is { } last && (ushort)(last + 1) != rtp.Sequence)
            {
                _gaps++;
            }

            if (_lastTimestamp is { } lastTs)
            {
                _steps++;
                if (rtp.Timestamp - lastTs == Tone.FrameSamples)
                {
                    _goodSteps++;
                }
            }

            _lastSequence = rtp.Sequence;
            _lastTimestamp = rtp.Timestamp;

            var pcm = new short[Tone.FrameSamples * 6];
            int samples;
            fixed (byte* data = packet.Slice(rtp.PayloadOffset, rtp.PayloadLength))
            fixed (short* output = pcm)
            {
                samples = OpusNative.Decode(_decoder, data, rtp.PayloadLength, output, pcm.Length, 0);
            }

            if (samples > 0)
            {
                _decoded++;
                _pcm.AddRange(pcm.AsSpan(0, samples));
            }
        }
    }

    public AnswererResult Result(bool connected, IReadOnlyList<string> errors)
    {
        lock (_gate)
        {
            // Descarta os primeiros 200 ms (pré-roll do codec) antes de medir.
            var skip = Math.Min(_pcm.Count, Tone.SampleRate / 5);
            var samples = _pcm.Skip(skip).ToArray();
            var tone = samples.Length > 0 ? Tone.Power(samples, Tone.Frequency) : 0;
            var offBand = samples.Length > 0 ? Tone.Power(samples, 1000) : 0;
            return new AnswererResult(
                connected,
                _packets,
                _decoded,
                _gaps,
                _steps == 0 ? 0 : (double)_goodSteps / _steps,
                Math.Min(1e12, offBand <= 0 ? (tone > 0 ? 1e12 : 0) : tone / offBand),
                Tone.Rms(samples),
                errors);
        }
    }

    public void Dispose() => OpusNative.DecoderDestroy(_decoder);
}

internal static class Exchange
{
    public static void Write(string dir, string name, string content)
    {
        var temp = Path.Combine(dir, name + ".tmp");
        File.WriteAllText(temp, content);
        File.Move(temp, Path.Combine(dir, name), overwrite: true);
    }

    public static string WaitFor(string dir, string name, TimeSpan timeout)
    {
        var path = Path.Combine(dir, name);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException($"{name} não chegou em {timeout.TotalSeconds:0} s");
    }
}
