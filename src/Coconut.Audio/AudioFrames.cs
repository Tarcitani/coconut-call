namespace Coconut.Audio;

/// <summary>Formato interno único: 48 kHz, mono, PCM 16 bits, quadros de 20 ms.</summary>
public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int FrameSamples = 960;
}

/// <summary>Agrupa pedaços de tamanho arbitrário (como entregues pelo WASAPI) em quadros fixos.</summary>
public sealed class FrameAssembler(int frameSamples, Action<ReadOnlySpan<short>> onFrame)
{
    private readonly short[] _frame = new short[frameSamples];
    private int _filled;

    public void Append(ReadOnlySpan<short> samples)
    {
        while (!samples.IsEmpty)
        {
            var take = Math.Min(samples.Length, _frame.Length - _filled);
            samples[..take].CopyTo(_frame.AsSpan(_filled));
            _filled += take;
            samples = samples[take..];
            if (_filled == _frame.Length)
            {
                onFrame(_frame);
                _filled = 0;
            }
        }
    }

    /// <summary>Silêncio do dispositivo (AUDCLNT_BUFFERFLAGS_SILENT) entra como zeros, mantendo a cadência.</summary>
    public void AppendSilence(int samples)
    {
        Span<short> zeros = stackalloc short[256];
        while (samples > 0)
        {
            var take = Math.Min(samples, zeros.Length);
            Append(zeros[..take]);
            samples -= take;
        }
    }
}

/// <summary>Entrega quadros fixos produzidos sob demanda em pedaços do tamanho pedido pelo dispositivo.</summary>
public sealed class FrameReader(int frameSamples, Action<Span<short>> produceFrame)
{
    private readonly short[] _frame = new short[frameSamples];
    private int _position = frameSamples;

    public void Read(Span<short> destination)
    {
        while (!destination.IsEmpty)
        {
            if (_position == _frame.Length)
            {
                produceFrame(_frame);
                _position = 0;
            }

            var take = Math.Min(destination.Length, _frame.Length - _position);
            _frame.AsSpan(_position, take).CopyTo(destination);
            _position += take;
            destination = destination[take..];
        }
    }
}

public static class AudioMixer
{
    public static void Accumulate(Span<int> accumulator, ReadOnlySpan<short> frame)
    {
        for (var i = 0; i < frame.Length; i++)
        {
            accumulator[i] += frame[i];
        }
    }

    /// <summary>Converte a soma para 16 bits com saturação (sem dar a volta em sinais altos).</summary>
    public static void Saturate(ReadOnlySpan<int> accumulator, Span<short> output)
    {
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = (short)Math.Clamp(accumulator[i], short.MinValue, short.MaxValue);
        }
    }

    public static double Rms(ReadOnlySpan<short> frame)
    {
        double sum = 0;
        foreach (var s in frame)
        {
            sum += (double)s * s;
        }

        return frame.IsEmpty ? 0 : Math.Sqrt(sum / frame.Length);
    }
}
