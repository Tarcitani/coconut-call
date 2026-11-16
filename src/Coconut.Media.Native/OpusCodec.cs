using Coconut.Media.Native.Interop;

namespace Coconut.Media.Native;

public static class OpusCodec
{
    public const int SampleRate = 48_000;

    /// <summary>20 ms a 48 kHz.</summary>
    public const int FrameSamples = 960;

    /// <summary>Maior quadro decodificável (120 ms).</summary>
    public const int MaxFrameSamples = 5760;
}

/// <summary>Codificador Opus mono, perfil de voz.</summary>
public sealed unsafe class OpusEncoder : IDisposable
{
    private IntPtr _handle;

    public OpusEncoder()
    {
        int error;
        _handle = OpusNative.EncoderCreate(OpusCodec.SampleRate, 1, OpusNative.ApplicationVoip, &error);
        if (_handle == IntPtr.Zero || error != 0)
        {
            throw new InvalidOperationException($"opus_encoder_create falhou ({error})");
        }
    }

    /// <summary>Codifica um quadro de 20 ms; devolve o tamanho escrito em <paramref name="output"/>.</summary>
    public int Encode(ReadOnlySpan<short> pcm, Span<byte> output)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        if (pcm.Length != OpusCodec.FrameSamples)
        {
            throw new ArgumentException($"Quadro deve ter {OpusCodec.FrameSamples} amostras.", nameof(pcm));
        }

        fixed (short* input = pcm)
        fixed (byte* data = output)
        {
            var length = OpusNative.Encode(_handle, input, pcm.Length, data, output.Length);
            return length > 0 ? length : throw new InvalidOperationException($"opus_encode falhou ({length})");
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            OpusNative.EncoderDestroy(_handle);
            _handle = IntPtr.Zero;
        }
    }
}

/// <summary>Decodificador Opus mono (um por par remoto).</summary>
public sealed unsafe class OpusDecoder : IDisposable
{
    private IntPtr _handle;

    public OpusDecoder()
    {
        int error;
        _handle = OpusNative.DecoderCreate(OpusCodec.SampleRate, 1, &error);
        if (_handle == IntPtr.Zero || error != 0)
        {
            throw new InvalidOperationException($"opus_decoder_create falhou ({error})");
        }
    }

    /// <summary>Decodifica um pacote; devolve o número de amostras escritas, ou negativo em erro.</summary>
    public int Decode(ReadOnlySpan<byte> packet, Span<short> pcm)
    {
        ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
        fixed (byte* data = packet)
        fixed (short* output = pcm)
        {
            return OpusNative.Decode(_handle, data, packet.Length, output, pcm.Length, 0);
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            OpusNative.DecoderDestroy(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
