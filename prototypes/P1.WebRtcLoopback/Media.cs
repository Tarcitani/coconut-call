using System.Buffers.Binary;

namespace Coconut.Prototypes.P1;

/// <summary>Leitura mínima de RTP (RFC 3550) para validar a mídia recebida.</summary>
internal readonly record struct RtpPacket(byte PayloadType, ushort Sequence, uint Timestamp, uint Ssrc, int PayloadOffset, int PayloadLength)
{
    public static bool TryParse(ReadOnlySpan<byte> packet, out RtpPacket rtp)
    {
        rtp = default;
        if (packet.Length < 12 || packet[0] >> 6 != 2)
        {
            return false;
        }

        var payloadType = (byte)(packet[1] & 0x7F);
        if (payloadType is >= 72 and <= 76)
        {
            return false; // RTCP multiplexado (RFC 5761)
        }

        var offset = 12 + (4 * (packet[0] & 0x0F));
        if ((packet[0] & 0x10) != 0)
        {
            if (packet.Length < offset + 4)
            {
                return false;
            }

            offset += 4 + (4 * BinaryPrimitives.ReadUInt16BigEndian(packet[(offset + 2)..]));
        }

        var padding = (packet[0] & 0x20) != 0 ? packet[^1] : 0;
        var length = packet.Length - offset - padding;
        if (length < 0)
        {
            return false;
        }

        rtp = new RtpPacket(
            payloadType,
            BinaryPrimitives.ReadUInt16BigEndian(packet[2..]),
            BinaryPrimitives.ReadUInt32BigEndian(packet[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(packet[8..]),
            offset,
            length);
        return true;
    }
}

internal static class Tone
{
    public const int SampleRate = 48_000;
    public const int FrameSamples = 960; // 20 ms
    public const double Frequency = 440.0;

    public static short[] Frame(int index)
    {
        var frame = new short[FrameSamples];
        for (var i = 0; i < FrameSamples; i++)
        {
            var t = (double)((index * FrameSamples) + i) / SampleRate;
            frame[i] = (short)(Math.Sin(2 * Math.PI * Frequency * t) * 8000);
        }

        return frame;
    }

    /// <summary>Potência relativa numa frequência (algoritmo de Goertzel).</summary>
    public static double Power(ReadOnlySpan<short> samples, double frequency)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / SampleRate);
        double s1 = 0, s2 = 0;
        foreach (var sample in samples)
        {
            var s0 = sample + (coefficient * s1) - s2;
            s2 = s1;
            s1 = s0;
        }

        return ((s1 * s1) + (s2 * s2) - (coefficient * s1 * s2)) / samples.Length;
    }

    public static double Rms(ReadOnlySpan<short> samples)
    {
        double sum = 0;
        foreach (var s in samples)
        {
            sum += (double)s * s;
        }

        return samples.Length == 0 ? 0 : Math.Sqrt(sum / samples.Length);
    }
}

/// <summary>Evidências extraídas do SDP sem expor endereços.</summary>
internal static class Sdp
{
    public static string CandidateType(string candidate)
    {
        var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.IndexOf(parts, "typ");
        return index >= 0 && index + 1 < parts.Length ? parts[index + 1] : "desconhecido";
    }

    public static SdpEvidence Inspect(string sdp)
    {
        var lines = sdp.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var candidateTypes = lines
            .Where(l => l.StartsWith("a=candidate:", StringComparison.Ordinal))
            .GroupBy(CandidateType)
            .ToDictionary(g => g.Key, g => g.Count());
        return new SdpEvidence(
            MediaProfileSavpf: lines.Any(l => l.StartsWith("m=audio", StringComparison.Ordinal) && l.Contains("UDP/TLS/RTP/SAVPF", StringComparison.Ordinal)),
            DtlsFingerprint: lines.Any(l => l.StartsWith("a=fingerprint:sha-256", StringComparison.Ordinal)),
            CandidateTypes: candidateTypes);
    }
}

internal sealed record SdpEvidence(bool MediaProfileSavpf, bool DtlsFingerprint, IReadOnlyDictionary<string, int> CandidateTypes);
