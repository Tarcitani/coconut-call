using System.Buffers.Binary;
using Coconut.Media.Native;
using Coconut.Signaling.Contracts;

namespace Coconut.Client.Screen;

public sealed record ScreenFrame(byte[] AnnexB, int Width, int Height, uint Timestamp, bool IsKeyframe);

/// <summary>RFC 6184 single NAL/FU-A; RFC 8285 extension binds EVERY packet to the full server-issued session.</summary>
public static class ScreenRtp
{
    public const int MaxFrameBytes = 4 * 1024 * 1024;
    public const int MaxPayloadBytes = 1100;
    public const int HeaderBytes = 40;
    public const byte PayloadType = 96;

    public static IReadOnlyList<byte[]> Packetize(ShareSessionId session, ScreenFrame frame, uint ssrc, ref ushort sequence)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.AnnexB.Length is 0 or > MaxFrameBytes || !ValidDimensions(frame.Width, frame.Height)) { throw new ArgumentException("Quadro inválido.", nameof(frame)); }
        var nals = Nals(frame.AnnexB);
        if (nals.Count > 256) { throw new ArgumentException("Muitas NALs no mesmo quadro.", nameof(frame)); }
        var packets = new List<byte[]>();
        for (var index = 0; index < nals.Count; index++)
        {
            var nal = frame.AnnexB.AsSpan(nals[index].Offset, nals[index].Length);
            if (nal.Length == 0 || (nal[0] & 0x1f) is 0 or >= 24) { throw new ArgumentException("NAL H.264 inválida.", nameof(frame)); }
            if (nal.Length <= MaxPayloadBytes)
            {
                var packet = Header(session, frame, ssrc, sequence++, nal.Length, index == nals.Count - 1);
                nal.CopyTo(packet.AsSpan(HeaderBytes)); packets.Add(packet);
            }
            else
            {
                for (var offset = 1; offset < nal.Length;)
                {
                    var count = Math.Min(MaxPayloadBytes - 2, nal.Length - offset);
                    var end = offset + count == nal.Length;
                    var packet = Header(session, frame, ssrc, sequence++, count + 2, end && index == nals.Count - 1);
                    packet[HeaderBytes] = (byte)((nal[0] & 0xe0) | 28);
                    packet[HeaderBytes + 1] = (byte)((nal[0] & 0x1f) | (offset == 1 ? 0x80 : 0) | (end ? 0x40 : 0));
                    nal.Slice(offset, count).CopyTo(packet.AsSpan(HeaderBytes + 2)); packets.Add(packet); offset += count;
                }
            }
        }
        return packets;
    }

    public static bool ValidDimensions(int width, int height) => width is >= 160 and <= 3840 && height is >= 90 and <= 2160 && width % 2 == 0 && height % 2 == 0;

    public static bool TryHeader(ReadOnlySpan<byte> packet, out ShareSessionId session, out int width, out int height, out RtpPacket rtp)
    {
        session = default; width = height = 0; rtp = default;
        // Our protocol uses exactly this extension layout; reject padding/CSRC tricks and RTCP.
        if (packet.Length <= HeaderBytes || packet[0] != 0x90 || !RtpPacket.TryParse(packet, out rtp) || rtp.PayloadType != PayloadType ||
            rtp.PayloadOffset != HeaderBytes || BinaryPrimitives.ReadUInt16BigEndian(packet[12..]) != 0x1000 ||
            BinaryPrimitives.ReadUInt16BigEndian(packet[14..]) != 6 || packet[16] != 1 || packet[17] != 20 || packet[38] != 0 || packet[39] != 0) { return false; }
        session = new ShareSessionId(new Guid(packet.Slice(18, 16), bigEndian: true));
        width = BinaryPrimitives.ReadUInt16BigEndian(packet[34..]); height = BinaryPrimitives.ReadUInt16BigEndian(packet[36..]);
        return ValidDimensions(width, height);
    }

    private static byte[] Header(ShareSessionId session, ScreenFrame frame, uint ssrc, ushort sequence, int payload, bool marker)
    {
        var packet = new byte[HeaderBytes + payload]; packet[0] = 0x90; packet[1] = (byte)(PayloadType | (marker ? 0x80 : 0));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), frame.Timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), ssrc);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(12), 0x1000); BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(14), 6);
        packet[16] = 1; packet[17] = 20; session.Value.TryWriteBytes(packet.AsSpan(18, 16), bigEndian: true, out _);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(34), (ushort)frame.Width);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(36), (ushort)frame.Height); return packet;
    }

    internal static List<(int Offset, int Length)> Nals(byte[] data)
    {
        var result = new List<(int, int)>();
        var start = -1;
        for (var i = 0; i + 2 < data.Length; i++)
        {
            if (data[i] != 0 || data[i + 1] != 0) { continue; }
            var length = data[i + 2] == 1 ? 3 : i + 3 < data.Length && data[i + 2] == 0 && data[i + 3] == 1 ? 4 : 0;
            if (length == 0) { continue; }
            if (start >= 0 && i > start) { result.Add((start, i - start)); }
            start = i + length; i += length - 1;
        }
        if (start >= 0 && start < data.Length) { result.Add((start, data.Length - start)); }
        if (result.Count != 0) { return result; }
        // Some MF hardware encoders produce AVCC length-prefixed samples.
        for (var offset = 0; offset + 4 <= data.Length;)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset)); offset += 4;
            if (length == 0 || length > data.Length - offset) { throw new ArgumentException("Amostra H.264 inválida.", nameof(data)); }
            result.Add((offset, (int)length)); offset += (int)length;
            if (offset == data.Length) { return result; }
        }
        throw new ArgumentException("Amostra H.264 truncada.", nameof(data));
    }
}

/// <summary>One bounded access unit. Late packets, stale sessions and unauthorized peers cannot reach the decoder.</summary>
public sealed class ScreenRtpReceiver
{
    private readonly object _gate = new();
    private readonly Dictionary<ushort, byte[]> _packets = [];
    private ParticipantId? _sharer;
    private ShareSessionId? _session;
    private uint? _timestamp;
    private ushort? _first, _last, _expected;
    private int _bytes, _width, _height;
    private bool _waitingKeyframe = true;
    private uint _ssrc;
    private long _received, _lost, _lastReceived, _lastLost;
    private ushort? _highest;
    private readonly HashSet<ushort> _missing = [];
    private uint _lastSenderReport;
    private long _senderReportArrival;

    public void Authorize(ParticipantId? sharer, ShareSessionId? session)
    {
        lock (_gate)
        {
            if (_sharer == sharer && _session == session) { return; }
            _sharer = sharer; _session = session; ResetFrame(); _expected = null; _waitingKeyframe = true;
            _received = _lost = _lastReceived = _lastLost = 0; _highest = null;
            _missing.Clear();
            _lastSenderReport = 0;
        }
    }

    public ScreenFrame? Push(ParticipantId from, ReadOnlySpan<byte> packet)
    {
        lock (_gate)
        {
            if (from != _sharer || _session is null || !ScreenRtp.TryHeader(packet, out var session, out var width, out var height, out var rtp) || session != _session) { return null; }
            if (_expected is { } expectedSequence && unchecked((short)(rtp.Sequence - expectedSequence)) < 0) { return null; }
            if (_timestamp == rtp.Timestamp && _packets.Count > 0 && (_width != width || _height != height || _ssrc != rtp.Ssrc)) { return null; }
            if (_timestamp is { } previous && previous != rtp.Timestamp)
            {
                if (unchecked((int)(rtp.Timestamp - previous)) <= 0) { return null; }
                if (_packets.Count > 0) { _waitingKeyframe = true; _expected = null; }
                ResetFrame();
            }
            _timestamp = rtp.Timestamp; _width = width; _height = height; _ssrc = rtp.Ssrc;
            if (_highest is { } highest)
            {
                var gap = unchecked((short)(rtp.Sequence - highest));
                if (gap > 0)
                {
                    _lost += gap - 1; _highest = rtp.Sequence;
                    if (_missing.Count + gap > 4096) { _missing.Clear(); }
                    if (gap < 4096) { for (var i = 1; i < gap; i++) { _missing.Add(unchecked((ushort)(highest + i))); } }
                }
                else if (_missing.Remove(rtp.Sequence)) { _lost--; }
            }
            else { _highest = rtp.Sequence; }
            if (_packets.ContainsKey(rtp.Sequence)) { return null; }
            if (_packets.Count >= 4096 || _bytes + rtp.PayloadLength > ScreenRtp.MaxFrameBytes) { ResetFrame(); _waitingKeyframe = true; return null; }
            var payload = packet.Slice(rtp.PayloadOffset, rtp.PayloadLength);
            var type = payload[0] & 0x1f;
            if ((payload[0] & 0x80) != 0 || type is 0 or >= 24 and not 28 ||
                type == 28 && (payload.Length < 3 || (payload[1] & 0x20) != 0 || (payload[1] & 0x1f) is 0 or >= 24))
            { ResetFrame(); _waitingKeyframe = true; return null; }
            _packets.Add(rtp.Sequence, payload.ToArray()); _bytes += payload.Length; _received++;
            if (_expected is { } expected) { _first = expected; }
            else if (type != 28 || (payload[1] & 0x80) != 0)
            {
                if (_first is null || unchecked((short)(rtp.Sequence - _first.Value)) < 0) { _first = rtp.Sequence; }
            }
            if ((packet[1] & 0x80) != 0) { _last = rtp.Sequence; }
            return Assemble();
        }
    }

    public byte[]? ReceiverReport(uint receiverSsrc)
    {
        lock (_gate)
        {
            if (_highest is null) { return null; }
            var received = _received - _lastReceived; var lost = Math.Max(0, _lost - _lastLost);
            _lastReceived = _received; _lastLost = _lost;
            var report = new byte[32]; report[0] = 0x81; report[1] = 201;
            BinaryPrimitives.WriteUInt16BigEndian(report.AsSpan(2), 7);
            BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(4), receiverSsrc);
            BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(8), _ssrc);
            report[12] = received + lost > 0 ? (byte)Math.Min(255, lost * 256 / (received + lost)) : (byte)0;
            BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(16), _highest.Value);
            if (_lastSenderReport != 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(24), _lastSenderReport);
                BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(28), (uint)Math.Min(uint.MaxValue, System.Diagnostics.Stopwatch.GetElapsedTime(_senderReportArrival).TotalSeconds * 65536));
            }
            return report;
        }
    }

    public void SenderReport(ParticipantId from, ReadOnlySpan<byte> packet)
    {
        lock (_gate)
        {
            if (from != _sharer || _session is null || packet.Length != 28 || packet[0] != 0x80 || packet[1] != 200 ||
                BinaryPrimitives.ReadUInt16BigEndian(packet[2..]) != 6 || BinaryPrimitives.ReadUInt32BigEndian(packet[4..]) != _ssrc) { return; }
            _lastSenderReport = (BinaryPrimitives.ReadUInt32BigEndian(packet[8..]) << 16) | (BinaryPrimitives.ReadUInt32BigEndian(packet[12..]) >> 16);
            _senderReportArrival = System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }

    private ScreenFrame? Assemble()
    {
        if (_first is not { } first || _last is not { } last) { return null; }
        var count = unchecked((ushort)(last - first)) + 1;
        if (count > 4096 || count > _packets.Count) { return null; }
        for (var i = 0; i < count; i++) { if (!_packets.ContainsKey(unchecked((ushort)(first + i)))) { return null; } }
        using var result = new MemoryStream(); var inFragment = false; var key = false; var sps = false; var pps = false; var nals = 0;
        for (var i = 0; i < count; i++)
        {
            var payload = _packets[unchecked((ushort)(first + i))]; var type = payload[0] & 0x1f;
            if (type != 28 || (payload[1] & 0x80) != 0) { if (++nals > 256) { ResetFrame(); _waitingKeyframe = true; return null; } }
            if (type == 28)
            {
                var start = (payload[1] & 0x80) != 0; var end = (payload[1] & 0x40) != 0;
                if (start == inFragment || start && end) { ResetFrame(); _waitingKeyframe = true; return null; }
                if (start) { result.Write([0, 0, 0, 1, (byte)((payload[0] & 0xe0) | (payload[1] & 0x1f))]); key |= (payload[1] & 0x1f) == 5; sps |= (payload[1] & 0x1f) == 7; pps |= (payload[1] & 0x1f) == 8; inFragment = true; }
                result.Write(payload.AsSpan(2)); if (end) { inFragment = false; }
            }
            else
            {
                if (inFragment) { ResetFrame(); _waitingKeyframe = true; return null; }
                result.Write([0, 0, 0, 1]); result.Write(payload); key |= type == 5; sps |= type == 7; pps |= type == 8;
            }
        }
        if (inFragment || result.Length > ScreenRtp.MaxFrameBytes || _waitingKeyframe && !(key && sps && pps)) { return null; }
        _waitingKeyframe = false; _expected = unchecked((ushort)(last + 1));
        var frame = new ScreenFrame(result.ToArray(), _width, _height, _timestamp!.Value, key);
        _packets.Clear(); _bytes = 0; _first = _last = null; return frame;
    }

    private void ResetFrame() { _packets.Clear(); _timestamp = null; _first = _last = null; _bytes = 0; }
}
