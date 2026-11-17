using Coconut.Client.Screen;
using Coconut.Signaling.Contracts;

namespace Coconut.Client.Tests;

public sealed class H264ParameterTests
{
    [Fact]
    public void A_late_joiner_can_start_from_a_subsequent_idr_without_the_original_parameter_packets()
    {
        var sets = new H264ParameterSets();
        sets.Normalize([0, 0, 0, 1, 0x67, 0x42, 0x20, 0x2a, 0, 0, 0, 1, 0x68, 0xce, 6, 0, 0, 0, 1, 0x65, 0x55]);
        var normalized = sets.Normalize([0, 0, 0, 1, 0x65, 0x77]);
        var peer = ParticipantId.New(); var session = ShareSessionId.New(); var receiver = new ScreenRtpReceiver(); receiver.Authorize(peer, session);
        ushort sequence = 100; ScreenFrame? result = null;
        foreach (var packet in ScreenRtp.Packetize(session, new ScreenFrame(normalized.AnnexB, 1280, 720, 9000, normalized.IsKeyframe), 77, ref sequence)) { result = receiver.Push(peer, packet) ?? result; }
        Assert.NotNull(result); Assert.True(result.IsKeyframe);
    }

    [Fact]
    public void Avcc_codec_extradata_and_length_prefixed_samples_are_normalized_for_decoder_and_transport()
    {
        byte[] extradata = [1, 0x42, 0x20, 0x2a, 0xff, 0xe1, 0, 4, 0x67, 0x42, 0x20, 0x2a, 1, 0, 3, 0x68, 0xce, 6];
        var normalized = new H264ParameterSets().Normalize([0, 0, 0, 2, 0x65, 0x77], extradata);
        Assert.True(normalized.IsKeyframe);
        Assert.Equal(new byte[] { 0, 0, 0, 1, 0x67, 0x42, 0x20, 0x2a, 0, 0, 0, 1, 0x68, 0xce, 6, 0, 0, 0, 1, 0x65, 0x77 }, normalized.AnnexB);
    }
}
