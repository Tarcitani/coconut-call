using System.Runtime.InteropServices;

namespace Coconut.Prototypes.P1.Native;

/// <summary>Subconjunto da API estável da libopus (opus.h).</summary>
internal static unsafe partial class OpusNative
{
    private const string Lib = "opus";

    public const int ApplicationVoip = 2048;

    [LibraryImport(Lib, EntryPoint = "opus_encoder_create")]
    public static partial IntPtr EncoderCreate(int sampleRate, int channels, int application, int* error);

    [LibraryImport(Lib, EntryPoint = "opus_encode")]
    public static partial int Encode(IntPtr encoder, short* pcm, int frameSize, byte* data, int maxDataBytes);

    [LibraryImport(Lib, EntryPoint = "opus_encoder_destroy")]
    public static partial void EncoderDestroy(IntPtr encoder);

    [LibraryImport(Lib, EntryPoint = "opus_decoder_create")]
    public static partial IntPtr DecoderCreate(int sampleRate, int channels, int* error);

    [LibraryImport(Lib, EntryPoint = "opus_decode")]
    public static partial int Decode(IntPtr decoder, byte* data, int length, short* pcm, int frameSize, int decodeFec);

    [LibraryImport(Lib, EntryPoint = "opus_decoder_destroy")]
    public static partial void DecoderDestroy(IntPtr decoder);
}
