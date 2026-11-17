using System.Buffers.Binary;

namespace Coconut.Client.Screen;

/// <summary>Normalize MF AVCC/Annex B and repeat SPS/PPS on IDR so late joiners never need the first frame.</summary>
public sealed class H264ParameterSets
{
    private byte[]? _sps, _pps;
    private int _lengthSize = 4;
    private bool _seeded;

    public (byte[] AnnexB, bool IsKeyframe) Normalize(ReadOnlySpan<byte> sample, ReadOnlySpan<byte> sequenceHeader = default)
    {
        if (sample.Length is 0 or > ScreenRtp.MaxFrameBytes) { throw new ArgumentException("Amostra H.264 inválida.", nameof(sample)); }
        if (!_seeded && !sequenceHeader.IsEmpty) { Seed(sequenceHeader); _seeded = true; }
        var data = sample.ToArray();
        var isAnnexB = data.Length >= 4 && data[0] == 0 && data[1] == 0 && (data[2] == 1 || data[2] == 0 && data[3] == 1);
        var nals = isAnnexB ? ScreenRtp.Nals(data) : LengthPrefixed(data, _lengthSize);
        var key = false; var hasSps = false; var hasPps = false;
        foreach (var (offset, length) in nals)
        {
            var type = data[offset] & 0x1f; key |= type == 5;
            if (type == 7) { _sps = data.AsSpan(offset, length).ToArray(); hasSps = true; }
            if (type == 8) { _pps = data.AsSpan(offset, length).ToArray(); hasPps = true; }
        }
        if (isAnnexB && (!key || hasSps && hasPps)) { return (data, key); }
        using var output = new MemoryStream();
        if (key && !hasSps && _sps is { } sps) { output.Write([0, 0, 0, 1]); output.Write(sps); }
        if (key && !hasPps && _pps is { } pps) { output.Write([0, 0, 0, 1]); output.Write(pps); }
        foreach (var (offset, length) in nals) { output.Write([0, 0, 0, 1]); output.Write(data.AsSpan(offset, length)); }
        if (output.Length > ScreenRtp.MaxFrameBytes) { throw new ArgumentException("Quadro H.264 excedeu o limite.", nameof(sample)); }
        return (output.ToArray(), key);
    }

    private void Seed(ReadOnlySpan<byte> header)
    {
        if (header.Length > 65536) { return; }
        if (header.Length >= 7 && header[0] == 1)
        {
            _lengthSize = (header[4] & 3) + 1; var offset = 6; var count = header[5] & 31;
            for (var i = 0; i < count; i++) { if (!ReadParameter(header, ref offset, true)) { return; } }
            if (offset >= header.Length) { return; }
            count = header[offset++];
            for (var i = 0; i < count; i++) { if (!ReadParameter(header, ref offset, false)) { return; } }
        }
        else if (header.Length >= 4 && header[0] == 0 && header[1] == 0)
        {
            var data = header.ToArray();
            foreach (var (offset, length) in ScreenRtp.Nals(data))
            {
                if ((data[offset] & 31) == 7) { _sps = data.AsSpan(offset, length).ToArray(); }
                if ((data[offset] & 31) == 8) { _pps = data.AsSpan(offset, length).ToArray(); }
            }
        }
    }

    private bool ReadParameter(ReadOnlySpan<byte> header, ref int offset, bool sps)
    {
        if (offset + 2 > header.Length) { return false; }
        var length = BinaryPrimitives.ReadUInt16BigEndian(header[offset..]); offset += 2;
        if (length == 0 || offset + length > header.Length) { return false; }
        if (sps) { _sps = header.Slice(offset, length).ToArray(); } else { _pps = header.Slice(offset, length).ToArray(); }
        offset += length; return true;
    }

    private static List<(int Offset, int Length)> LengthPrefixed(byte[] data, int size)
    {
        var nals = new List<(int, int)>();
        for (var offset = 0; offset < data.Length;)
        {
            if (data.Length - offset < size) { throw new ArgumentException("Amostra AVCC truncada.", nameof(data)); }
            uint length = 0; for (var i = 0; i < size; i++) { length = (length << 8) | data[offset++]; }
            if (length == 0 || length > data.Length - offset) { throw new ArgumentException("Tamanho de NAL inválido.", nameof(data)); }
            nals.Add((offset, (int)length)); offset += (int)length;
        }
        return nals;
    }
}
