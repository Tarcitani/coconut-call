namespace Coconut.Audio.Tests;

public class AudioFramesTests
{
    [Fact]
    public void Assembler_emits_fixed_frames_from_irregular_chunks()
    {
        var frames = new List<short[]>();
        var assembler = new FrameAssembler(4, f => frames.Add(f.ToArray()));

        assembler.Append([1, 2, 3]);
        assembler.Append([4, 5]);
        assembler.Append([6, 7, 8, 9]);

        Assert.Equal([[1, 2, 3, 4], [5, 6, 7, 8]], frames);
    }

    [Fact]
    public void Assembler_inserts_device_silence_as_zeros()
    {
        var frames = new List<short[]>();
        var assembler = new FrameAssembler(4, f => frames.Add(f.ToArray()));

        assembler.Append([1]);
        assembler.AppendSilence(3);

        Assert.Equal([1, 0, 0, 0], Assert.Single(frames));
    }

    [Fact]
    public void Reader_serves_arbitrary_chunk_sizes_from_fixed_frames()
    {
        var next = 0;
        var reader = new FrameReader(4, f =>
        {
            for (var i = 0; i < f.Length; i++)
            {
                f[i] = (short)next++;
            }
        });
        var output = new short[10];

        reader.Read(output.AsSpan(0, 3));
        reader.Read(output.AsSpan(3, 7));

        Assert.Equal(Enumerable.Range(0, 10).Select(i => (short)i), output);
    }

    [Fact]
    public void Mixer_sums_and_saturates_instead_of_wrapping()
    {
        var accumulator = new int[3];
        AudioMixer.Accumulate(accumulator, [30000, -30000, 100]);
        AudioMixer.Accumulate(accumulator, [10000, -10000, 50]);
        var output = new short[3];

        AudioMixer.Saturate(accumulator, output);

        Assert.Equal([short.MaxValue, short.MinValue, (short)150], output);
    }

    [Fact]
    public void Rms_of_full_scale_square_wave_is_its_amplitude()
    {
        Assert.Equal(1000, AudioMixer.Rms([1000, -1000, 1000, -1000]), precision: 6);
        Assert.Equal(0, AudioMixer.Rms([]));
    }
}
