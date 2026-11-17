using System.Buffers.Binary;
using Coconut.Client.Screen;
using Coconut.Signaling.Contracts;

namespace Coconut.Client.Tests;

public sealed class ScreenRtpTests
{
    private readonly ParticipantId _sender = ParticipantId.New();
    private readonly ShareSessionId _session = ShareSessionId.New();

    [Theory]
    [InlineData(100, false, 1)]
    [InlineData(50_000, false, 65530)]
    [InlineData(50_000, true, 65530)]
    public void Round_trip_reassembles_single_and_fragmented_nals_across_sequence_wrap(int bytes, bool reverse, int initial)
    {
        var frame = Keyframe(bytes);
        var receiver = Receiver(); ushort sequence = (ushort)initial;
        var packets = ScreenRtp.Packetize(_session, frame, 77, ref sequence);
        ScreenFrame? result = null;
        foreach (var packet in reverse ? packets.Reverse() : packets) { result = receiver.Push(_sender, packet) ?? result; }
        Assert.NotNull(result); Assert.Equal(frame.AnnexB, result.AnnexB); Assert.True(result.IsKeyframe);
        Assert.Equal(1280, result.Width); Assert.Equal(720, result.Height);
        Assert.All(packets, p => Assert.True(p.Length <= ScreenRtp.HeaderBytes + ScreenRtp.MaxPayloadBytes));
    }

    [Fact]
    public void Other_peer_or_previous_session_is_rejected_even_with_valid_h264()
    {
        var receiver = Receiver(); ushort sequence = 0;
        var packets = ScreenRtp.Packetize(_session, Keyframe(4000), 77, ref sequence);
        Assert.All(packets, p => Assert.Null(receiver.Push(ParticipantId.New(), p)));
        receiver.Authorize(_sender, ShareSessionId.New());
        Assert.All(packets, p => Assert.Null(receiver.Push(_sender, p)));
    }

    [Fact]
    public void Handover_discards_partial_old_frame_and_accepts_new_owner_only()
    {
        var receiver = Receiver(); ushort sequence = 0;
        var old = ScreenRtp.Packetize(_session, Keyframe(4000), 77, ref sequence);
        receiver.Push(_sender, old[0]);
        var next = ParticipantId.New(); var session = ShareSessionId.New(); receiver.Authorize(next, session);
        Assert.All(old, packet => Assert.Null(receiver.Push(_sender, packet)));
        ScreenFrame? result = null;
        foreach (var packet in ScreenRtp.Packetize(session, Keyframe(4000), 88, ref sequence)) { result = receiver.Push(next, packet) ?? result; }
        Assert.NotNull(result);
    }

    [Fact]
    public void Missing_fragment_never_reaches_decoder_and_next_keyframe_recovers()
    {
        var receiver = Receiver(); ushort sequence = 0;
        var broken = ScreenRtp.Packetize(_session, Keyframe(5000), 77, ref sequence);
        foreach (var packet in broken.Where((_, i) => i != 3)) { Assert.Null(receiver.Push(_sender, packet)); }
        ScreenFrame? recovered = null;
        foreach (var packet in ScreenRtp.Packetize(_session, Keyframe(5000) with { Timestamp = 18000 }, 77, ref sequence)) { recovered = receiver.Push(_sender, packet) ?? recovered; }
        Assert.NotNull(recovered);
    }

    [Fact]
    public void Duplicate_packets_after_delivery_do_not_break_next_frame()
    {
        var receiver = Receiver(); ushort sequence = 0;
        var first = ScreenRtp.Packetize(_session, Keyframe(5000), 77, ref sequence);
        foreach (var packet in first) { receiver.Push(_sender, packet); }
        Assert.All(first, packet => Assert.Null(receiver.Push(_sender, packet)));
        ScreenFrame? second = null;
        foreach (var packet in ScreenRtp.Packetize(_session, Keyframe(5000) with { Timestamp = 18000 }, 77, ref sequence)) { second = receiver.Push(_sender, packet) ?? second; }
        Assert.NotNull(second);
    }

    [Fact]
    public void Revocation_rejects_remaining_packets_and_clears_receiver_report()
    {
        var receiver = Receiver(); ushort sequence = 0;
        var packets = ScreenRtp.Packetize(_session, Keyframe(4000), 77, ref sequence);
        receiver.Push(_sender, packets[0]); receiver.Authorize(null, null);
        Assert.All(packets, p => Assert.Null(receiver.Push(_sender, p)));
        Assert.Null(receiver.ReceiverReport(1));
    }

    [Theory]
    [InlineData(0)] [InlineData(12)] [InlineData(14)] [InlineData(16)] [InlineData(17)] [InlineData(38)]
    public void Malformed_rtp_extensions_are_rejected(int offset)
    {
        ushort sequence = 0; var packet = ScreenRtp.Packetize(_session, Keyframe(100), 77, ref sequence)[0];
        packet[offset] ^= 0xff;
        Assert.False(ScreenRtp.TryHeader(packet, out _, out _, out _, out _));
    }

    [Fact]
    public void Odd_or_oversized_dimensions_are_rejected_before_decoder_allocation()
    {
        ushort sequence = 0; var packet = ScreenRtp.Packetize(_session, Keyframe(100), 77, ref sequence)[0];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(34), 65000);
        Assert.Null(Receiver().Push(_sender, packet));
        Assert.Throws<ArgumentException>(() => ScreenRtp.Packetize(_session, Keyframe(100) with { Width = 1279 }, 77, ref sequence));
    }

    [Fact]
    public void Interframes_are_not_delivered_before_a_keyframe_with_parameter_sets()
    {
        ushort sequence = 0;
        var packet = ScreenRtp.Packetize(_session, new ScreenFrame([0, 0, 0, 1, 0x41, 9, 9], 1280, 720, 9000, false), 77, ref sequence)[0];
        Assert.Null(Receiver().Push(_sender, packet));
    }

    private ScreenRtpReceiver Receiver() { var receiver = new ScreenRtpReceiver(); receiver.Authorize(_sender, _session); return receiver; }

    private static ScreenFrame Keyframe(int bytes)
    {
        using var stream = new MemoryStream(); stream.Write([0, 0, 0, 1, 0x67, 0x42, 0x20, 0x2a]);
        stream.Write([0, 0, 0, 1, 0x68, 0xce, 6]); stream.Write([0, 0, 0, 1, 0x65]);
        stream.Write(Enumerable.Repeat((byte)0x55, bytes).ToArray());
        return new ScreenFrame(stream.ToArray(), 1280, 720, 9000, true);
    }
}
