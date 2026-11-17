using Coconut.Audio;
using Coconut.Media.Native;
using Coconut.Signaling.Contracts;

namespace Coconut.Client.Voice;

/// <summary>
/// Malha → jitter buffer e decodificador por par → mixer → saída. <see cref="Push"/> é chamado nas threads de
/// rede; <see cref="Produce"/> no relógio da reprodução. Decodificadores só são usados sob o lock.
/// </summary>
public sealed class VoiceReceiver : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ParticipantId, PeerVoice> _peers = [];
    private readonly HashSet<ParticipantId> _departed = [];
    private readonly int[] _mix = new int[AudioFormat.FrameSamples];
    private readonly short[] _decoded = new short[OpusCodec.MaxFrameSamples];
    private long _framesProduced;

    /// <summary>Quadros de 20 ms entregues à saída desde o início (prova de que a reprodução está rodando).</summary>
    public long FramesProduced => Interlocked.Read(ref _framesProduced);

    /// <summary>Nível RMS do último quadro reproduzido de cada par (indicador de fala).</summary>
    public IReadOnlyDictionary<ParticipantId, double> Levels
    {
        get
        {
            lock (_gate)
            {
                return _peers.ToDictionary(p => p.Key, p => p.Value.LastLevel);
            }
        }
    }

    public IReadOnlyDictionary<ParticipantId, JitterStats> Stats
    {
        get
        {
            lock (_gate)
            {
                return _peers.ToDictionary(p => p.Key, p => p.Value.Jitter.Stats);
            }
        }
    }
    public IReadOnlySet<ParticipantId> DecodedPeers
    {
        get { lock (_gate) { return _peers.Where(p => p.Value.HasDecoded).Select(p => p.Key).ToHashSet(); } }
    }

    /// <summary>Pacote RTP de um par. Só Opus (PT 111) é aceito; o resto é ignorado.</summary>
    public void Push(ParticipantId from, ReadOnlySpan<byte> rtpPacket)
    {
        if (!RtpPacket.TryParse(rtpPacket, out var rtp) || rtp.PayloadType != RtcPeer.OpusPayloadType || rtp.PayloadLength <= 0)
        {
            return;
        }

        PeerVoice voice;
        lock (_gate)
        {
            if (_departed.Contains(from))
            {
                return; // pacote atrasado de quem já saiu
            }

            if (!_peers.TryGetValue(from, out voice!))
            {
                voice = new PeerVoice();
                _peers[from] = voice;
            }
        }

        voice.Jitter.Push(rtp.Sequence, rtpPacket.Slice(rtp.PayloadOffset, rtp.PayloadLength));
    }

    public void Remove(ParticipantId peer)
    {
        lock (_gate)
        {
            _departed.Add(peer);
            if (_peers.Remove(peer, out var voice))
            {
                voice.Decoder.Dispose();
            }
        }
    }

    /// <summary>Produz o próximo quadro de 20 ms da mistura de todos os pares.</summary>
    public void Produce(Span<short> output)
    {
        Interlocked.Increment(ref _framesProduced);
        Array.Clear(_mix);
        lock (_gate)
        {
            foreach (var voice in _peers.Values)
            {
                var frame = voice.Jitter.Pull();
                var samples = frame.Kind switch
                {
                    JitterFrameKind.Packet => voice.Decoder.Decode(frame.Payload!, _decoded),
                    JitterFrameKind.Lost => voice.Decoder.DecodeLost(_decoded.AsSpan(0, AudioFormat.FrameSamples)),
                    _ => 0,
                };

                var pcm = _decoded.AsSpan(0, Math.Clamp(samples, 0, AudioFormat.FrameSamples));
                if (frame.Kind == JitterFrameKind.Packet && samples > 0) { voice.HasDecoded = true; }
                voice.LastLevel = AudioMixer.Rms(pcm);
                AudioMixer.Accumulate(_mix, pcm);
            }
        }

        AudioMixer.Saturate(_mix, output);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var voice in _peers.Values)
            {
                voice.Decoder.Dispose();
            }

            _peers.Clear();
        }
    }

    private sealed class PeerVoice
    {
        public JitterBuffer Jitter { get; } = new();

        public OpusDecoder Decoder { get; } = new();

        public double LastLevel { get; set; }
        public bool HasDecoded { get; set; }
    }
}
