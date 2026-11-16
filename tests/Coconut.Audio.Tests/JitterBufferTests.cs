namespace Coconut.Audio.Tests;

public class JitterBufferTests
{
    private static byte[] P(int n) => [(byte)n];

    private static void PushRange(JitterBuffer buffer, int from, int count)
    {
        for (var i = 0; i < count; i++)
        {
            buffer.Push((ushort)(from + i), P(from + i));
        }
    }

    [Fact]
    public void Silence_until_target_depth_is_buffered()
    {
        var buffer = new JitterBuffer(targetFrames: 3);
        PushRange(buffer, 10, 2);

        Assert.Equal(JitterFrameKind.Silence, buffer.Pull().Kind);

        buffer.Push(12, P(12));
        Assert.Equal(new byte[] { 10 }, buffer.Pull().Payload);
    }

    [Fact]
    public void Reordered_packets_play_in_sequence()
    {
        var buffer = new JitterBuffer(targetFrames: 3);
        buffer.Push(3, P(3));
        buffer.Push(1, P(1));
        buffer.Push(2, P(2));

        Assert.Equal([1, 2, 3], Enumerable.Range(0, 3).Select(_ => (int)buffer.Pull().Payload![0]));
    }

    [Fact]
    public void Gap_is_concealed_and_playback_continues_with_next_packet()
    {
        var buffer = new JitterBuffer(targetFrames: 2);
        buffer.Push(1, P(1));
        buffer.Push(3, P(3));

        Assert.Equal(JitterFrameKind.Packet, buffer.Pull().Kind);
        Assert.Equal(JitterFrameKind.Lost, buffer.Pull().Kind);
        Assert.Equal(new byte[] { 3 }, buffer.Pull().Payload);
        Assert.Equal(1, buffer.Stats.Concealed);
    }

    [Fact]
    public void Late_and_duplicate_packets_are_discarded()
    {
        var buffer = new JitterBuffer(targetFrames: 2);
        PushRange(buffer, 5, 2);
        buffer.Pull();

        buffer.Push(5, P(5));
        buffer.Push(6, P(6));

        Assert.Equal(1, buffer.Stats.Late);
        Assert.Equal(1, buffer.Stats.Duplicates);
    }

    [Fact]
    public void Brief_underrun_is_concealed_then_falls_back_to_silence()
    {
        var buffer = new JitterBuffer(targetFrames: 1, maxConcealedFrames: 2);
        buffer.Push(1, P(1));
        buffer.Pull();

        Assert.Equal(JitterFrameKind.Lost, buffer.Pull().Kind);
        Assert.Equal(JitterFrameKind.Lost, buffer.Pull().Kind);
        Assert.Equal(JitterFrameKind.Silence, buffer.Pull().Kind);
        Assert.Equal(1, buffer.Stats.Underruns);
    }

    [Fact]
    public void After_silence_new_packets_rebuffer_from_their_own_sequence()
    {
        var buffer = new JitterBuffer(targetFrames: 2, maxConcealedFrames: 0);
        PushRange(buffer, 1, 2);
        buffer.Pull();
        buffer.Pull();
        Assert.Equal(JitterFrameKind.Silence, buffer.Pull().Kind);

        // O par voltou a falar (ex.: saiu do mudo) com sequência contínua.
        PushRange(buffer, 3, 2);

        Assert.Equal(new byte[] { 3 }, buffer.Pull().Payload);
    }

    [Fact]
    public void Overflow_drops_oldest_to_bound_latency()
    {
        var buffer = new JitterBuffer(targetFrames: 3, maxFrames: 5);
        PushRange(buffer, 1, 3);
        PushRange(buffer, 4, 3); // 6 > 5

        Assert.Equal(3, buffer.Stats.Dropped);
        Assert.Equal(new byte[] { 4 }, buffer.Pull().Payload);
    }

    [Fact]
    public void Sequence_wraparound_keeps_order()
    {
        var buffer = new JitterBuffer(targetFrames: 3);
        buffer.Push(0, P(0));
        buffer.Push(65535, P(255));
        buffer.Push(1, P(1));

        Assert.Equal([255, 0, 1], Enumerable.Range(0, 3).Select(_ => (int)buffer.Pull().Payload![0]));
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(0, 65535, 1)]
    [InlineData(65535, 0, -1)]
    [InlineData(100, 200, -100)]
    public void Sequence_difference_handles_wraparound(int a, int b, int expected) =>
        Assert.Equal(expected, JitterBuffer.Diff((ushort)a, (ushort)b));
}
