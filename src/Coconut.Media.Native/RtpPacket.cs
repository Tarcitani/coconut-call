using System.Buffers.Binary;

namespace Coconut.Media.Native;

/// <summary>Cabeçalho RTP (RFC 3550) e posição da carga útil; RTCP multiplexado é ignorado.</summary>
public readonly record struct RtpPacket(byte PayloadType, ushort Sequence, uint Timestamp, uint Ssrc, int PayloadOffset, int PayloadLength)
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
            return false; // RTCP (RFC 5761)
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
        if (offset > packet.Length || length < 0)
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
