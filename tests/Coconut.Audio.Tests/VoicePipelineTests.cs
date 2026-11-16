using System.Buffers.Binary;
using Coconut.Client.Voice;
using Coconut.Signaling.Contracts;

namespace Coconut.Audio.Tests;

/// <summary>Transmissor → RTP → receptor com Opus real: o que importa é o áudio decodificado do outro lado.</summary>
public class VoicePipelineTests
{
    private static short[] Tone(double frequency, int frameIndex, double amplitude = 8000)
    {
        var frame = new short[AudioFormat.FrameSamples];
        for (var i = 0; i < frame.Length; i++)
        {
            var t = (double)((frameIndex * frame.Length) + i) / AudioFormat.SampleRate;
            frame[i] = (short)(Math.Sin(2 * Math.PI * frequency * t) * amplitude);
        }

        return frame;
    }

    private static double Power(ReadOnlySpan<short> samples, double frequency)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / AudioFormat.SampleRate);
        double s1 = 0, s2 = 0;
        foreach (var x in samples)
        {
            var s0 = x + (coefficient * s1) - s2;
            s2 = s1;
            s1 = s0;
        }

        return ((s1 * s1) + (s2 * s2) - (coefficient * s1 * s2)) / samples.Length;
    }

    /// <summary>Simula a trilha: empacota cada quadro Opus em RTP e entrega ao receptor como vindo de <paramref name="from"/>.</summary>
    private sealed class Wire(VoiceReceiver receiver, ParticipantId from)
    {
        private ushort _sequence = 1000;

        public List<uint> Timestamps { get; } = [];

        public bool DropNext { get; set; }

        public int Send(ReadOnlySpan<byte> opus, uint timestamp)
        {
            Timestamps.Add(timestamp);
            var packet = new byte[12 + opus.Length];
            packet[0] = 0x80;
            packet[1] = 111;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), _sequence++);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), timestamp);
            BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), 42);
            opus.CopyTo(packet.AsSpan(12));
            if (DropNext)
            {
                DropNext = false;
                return 0;
            }

            receiver.Push(from, packet);
            return 1;
        }
    }

    /// <summary>Captura e reprodução andam no mesmo ritmo: um quadro capturado, um quadro reproduzido.</summary>
    private static List<short[]> Run(VoiceTransmitter tx, VoiceReceiver rx, int frames, double frequency, int startIndex = 0)
    {
        var output = new List<short[]>();
        for (var i = 0; i < frames; i++)
        {
            tx.OnCapturedFrame(Tone(frequency, startIndex + i));
            var played = new short[AudioFormat.FrameSamples];
            rx.Produce(played);
            output.Add(played);
        }

        return output;
    }

    [Fact]
    public void Muted_microphone_content_never_reaches_the_encoder_or_the_wire()
    {
        using var rx = new VoiceReceiver();
        var wire = new Wire(rx, ParticipantId.New());
        using var tx = new VoiceTransmitter(wire.Send) { IsMuted = true };

        var played = Run(tx, rx, 50, 440);

        Assert.Empty(wire.Timestamps);
        Assert.Equal(new TransmitStats(50, 0, 50, 0), tx.Stats);
        Assert.All(played, frame => Assert.Equal(0, AudioMixer.Rms(frame)));
    }

    [Fact]
    public void Unmuted_tone_arrives_decoded_and_recognizable()
    {
        using var rx = new VoiceReceiver();
        var wire = new Wire(rx, ParticipantId.New());
        using var tx = new VoiceTransmitter(wire.Send);

        var played = Run(tx, rx, 50, 440).Skip(10).SelectMany(f => f).ToArray();

        Assert.True(AudioMixer.Rms(played) > 1000, $"RMS {AudioMixer.Rms(played)}");
        Assert.True(Power(played, 440) > 100 * Power(played, 1000));
    }

    [Fact]
    public void Muting_mid_call_silences_the_remote_output_and_unmuting_restores_it()
    {
        using var rx = new VoiceReceiver();
        var wire = new Wire(rx, ParticipantId.New());
        using var tx = new VoiceTransmitter(wire.Send);

        Run(tx, rx, 20, 440);
        tx.IsMuted = true;
        var mutedPlayback = Run(tx, rx, 30, 440, startIndex: 20);
        tx.IsMuted = false;
        var resumed = Run(tx, rx, 30, 440, startIndex: 50);

        // Após esvaziar o buffer e a breve ocultação de perda, nada do microfone é ouvido.
        Assert.All(mutedPlayback.Skip(12), frame => Assert.Equal(0, AudioMixer.Rms(frame)));
        Assert.True(AudioMixer.Rms(resumed.Skip(10).SelectMany(f => f).ToArray()) > 1000);
    }

    [Fact]
    public void Rtp_timestamp_keeps_advancing_while_muted()
    {
        using var rx = new VoiceReceiver();
        var wire = new Wire(rx, ParticipantId.New());
        using var tx = new VoiceTransmitter(wire.Send);

        Run(tx, rx, 10, 440);
        tx.IsMuted = true;
        Run(tx, rx, 10, 440);
        tx.IsMuted = false;
        Run(tx, rx, 1, 440);

        Assert.Equal(11, wire.Timestamps.Count);
        Assert.Equal((uint)(960 * 9), wire.Timestamps[9] - wire.Timestamps[0]);
        Assert.Equal((uint)(960 * 20), wire.Timestamps[10] - wire.Timestamps[0]);
    }

    [Fact]
    public void Two_peers_are_mixed_and_a_removed_peer_stops_contributing()
    {
        using var rx = new VoiceReceiver();
        var ana = ParticipantId.New();
        var bia = ParticipantId.New();
        var wireA = new Wire(rx, ana);
        var wireB = new Wire(rx, bia);
        using var txA = new VoiceTransmitter(wireA.Send);
        using var txB = new VoiceTransmitter(wireB.Send);

        var mixed = new List<short>();
        for (var i = 0; i < 40; i++)
        {
            txA.OnCapturedFrame(Tone(440, i, 5000));
            txB.OnCapturedFrame(Tone(1000, i, 5000));
            var frame = new short[AudioFormat.FrameSamples];
            rx.Produce(frame);
            if (i >= 10)
            {
                mixed.AddRange(frame);
            }
        }

        var both = mixed.ToArray();
        Assert.True(Power(both, 440) > 100 * Power(both, 700));
        Assert.True(Power(both, 1000) > 100 * Power(both, 700));

        rx.Remove(bia);
        txB.OnCapturedFrame(Tone(1000, 40, 5000)); // pacote atrasado de quem saiu
        var afterRemoval = new List<short>();
        for (var i = 41; i < 61; i++)
        {
            txA.OnCapturedFrame(Tone(440, i, 5000));
            var frame = new short[AudioFormat.FrameSamples];
            rx.Produce(frame);
            afterRemoval.AddRange(frame);
        }

        var onlyAna = afterRemoval.ToArray();
        Assert.True(Power(onlyAna, 440) > 1000 * Power(onlyAna, 1000));
        Assert.False(rx.Stats.ContainsKey(bia));
    }

    [Fact]
    public void Lost_packet_is_concealed_instead_of_dropping_to_silence()
    {
        using var rx = new VoiceReceiver();
        var peer = ParticipantId.New();
        var wire = new Wire(rx, peer);
        using var tx = new VoiceTransmitter(wire.Send);

        Run(tx, rx, 10, 440);
        wire.DropNext = true;
        var played = Run(tx, rx, 10, 440, startIndex: 10);

        Assert.Equal(1, rx.Stats[peer].Concealed);
        Assert.All(played, frame => Assert.True(AudioMixer.Rms(frame) > 100));
    }
}
