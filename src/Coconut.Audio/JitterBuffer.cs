namespace Coconut.Audio;

public enum JitterFrameKind
{
    /// <summary>Pacote disponível para decodificar.</summary>
    Packet,

    /// <summary>Pacote ausente: o decodificador deve ocultar a perda (PLC).</summary>
    Lost,

    /// <summary>Nada a reproduzir (ainda armazenando, ou o par parou de enviar, ex.: mudo).</summary>
    Silence,
}

public readonly record struct JitterFrame(JitterFrameKind Kind, byte[]? Payload);

public readonly record struct JitterStats(long Received, long Late, long Duplicates, long Dropped, long Concealed, long Underruns, int Buffered);

/// <summary>
/// Buffer de jitter de um par: quadros de 20 ms indexados pelo número de sequência RTP (16 bits, com volta).
/// Push é chamado na thread de rede; Pull no relógio de reprodução. Thread-safe.
/// </summary>
public sealed class JitterBuffer(int targetFrames = 3, int maxFrames = 12, int maxConcealedFrames = 5)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ushort, byte[]> _packets = [];
    private bool _playing;
    private ushort _next;
    private int _concealedRun;
    private long _received;
    private long _late;
    private long _duplicates;
    private long _dropped;
    private long _concealed;
    private long _underruns;

    public JitterStats Stats
    {
        get
        {
            lock (_gate)
            {
                return new JitterStats(_received, _late, _duplicates, _dropped, _concealed, _underruns, _packets.Count);
            }
        }
    }

    public void Push(ushort sequence, ReadOnlySpan<byte> payload)
    {
        lock (_gate)
        {
            if (_playing && Diff(sequence, _next) < 0)
            {
                _late++;
                return;
            }

            if (!_packets.TryAdd(sequence, payload.ToArray()))
            {
                _duplicates++;
                return;
            }

            _received++;
            if (!_playing)
            {
                if (_packets.Count >= targetFrames)
                {
                    _next = Oldest();
                    _playing = true;
                    _concealedRun = 0;
                }
            }
            else if (_packets.Count > maxFrames)
            {
                DropToTarget();
            }
        }
    }

    public JitterFrame Pull()
    {
        lock (_gate)
        {
            if (!_playing)
            {
                return new JitterFrame(JitterFrameKind.Silence, null);
            }

            if (_packets.Remove(_next, out var payload))
            {
                _next++;
                _concealedRun = 0;
                return new JitterFrame(JitterFrameKind.Packet, payload);
            }

            // Lacuna com pacotes posteriores, ou falta breve: oculta a perda e avança.
            if (_packets.Count > 0 || _concealedRun < maxConcealedFrames)
            {
                _next++;
                _concealedRun++;
                _concealed++;
                return new JitterFrame(JitterFrameKind.Lost, null);
            }

            // O par parou de enviar (mudo, saída, rede): silêncio e novo pré-armazenamento.
            _playing = false;
            _underruns++;
            return new JitterFrame(JitterFrameKind.Silence, null);
        }
    }

    /// <summary>Diferença com volta em 16 bits: positiva se <paramref name="a"/> vem depois de <paramref name="b"/>.</summary>
    internal static int Diff(ushort a, ushort b) => (short)(a - b);

    private ushort Oldest()
    {
        var oldest = _packets.Keys.First();
        foreach (var key in _packets.Keys)
        {
            if (Diff(key, oldest) < 0)
            {
                oldest = key;
            }
        }

        return oldest;
    }

    /// <summary>Latência acumulada demais: descarta os mais antigos até o alvo.</summary>
    private void DropToTarget()
    {
        while (_packets.Count > targetFrames)
        {
            var oldest = Oldest();
            _packets.Remove(oldest);
            _dropped++;
        }

        _next = Oldest();
    }
}
