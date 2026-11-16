using Coconut.Audio;
using Coconut.Media.Native;

namespace Coconut.Client.Voice;

/// <summary>Envia um quadro Opus já codificado; devolve quantos pares o receberam.</summary>
public delegate int OpusFrameSender(ReadOnlySpan<byte> opusFrame, uint rtpTimestamp);

public readonly record struct TransmitStats(long FramesCaptured, long FramesEncoded, long FramesSuppressedByMute, long Deliveries);

/// <summary>
/// Microfone → porta de mudo → Opus → malha. Com mudo, o quadro capturado é descartado <b>antes</b> do
/// codificador: nenhum conteúdo do microfone vira pacote (requisito AUD-1, validado sobre a mídia).
/// O timestamp RTP avança mesmo em mudo, mantendo o relógio de mídia contínuo para os receptores.
/// Chamado na thread de captura.
/// </summary>
public sealed class VoiceTransmitter(OpusFrameSender send) : IDisposable
{
    private readonly OpusEncoder _encoder = new();
    private readonly byte[] _packet = new byte[1275]; // maior pacote Opus
    private uint _timestamp = (uint)Random.Shared.NextInt64(uint.MaxValue);
    private long _captured;
    private long _encoded;
    private long _suppressed;
    private long _deliveries;
    private volatile bool _muted;

    public bool IsMuted
    {
        get => _muted;
        set => _muted = value;
    }

    public TransmitStats Stats => new(
        Interlocked.Read(ref _captured),
        Interlocked.Read(ref _encoded),
        Interlocked.Read(ref _suppressed),
        Interlocked.Read(ref _deliveries));

    /// <summary>Nível RMS do último quadro capturado (para indicador de microfone), mesmo em mudo.</summary>
    public double LastCaptureLevel { get; private set; }

    public void OnCapturedFrame(ReadOnlySpan<short> frame)
    {
        Interlocked.Increment(ref _captured);
        LastCaptureLevel = AudioMixer.Rms(frame);
        var timestamp = _timestamp;
        _timestamp += AudioFormat.FrameSamples;

        if (_muted)
        {
            Interlocked.Increment(ref _suppressed);
            return;
        }

        var length = _encoder.Encode(frame, _packet);
        Interlocked.Increment(ref _encoded);
        Interlocked.Add(ref _deliveries, send(_packet.AsSpan(0, length), timestamp));
    }

    public void Dispose() => _encoder.Dispose();
}
