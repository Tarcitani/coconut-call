using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using Coconut.Client.Screen;
using Coconut.Signaling.Contracts;

namespace Coconut.Client;

public sealed partial class MeshSession
{
    private sealed record Authorization(ParticipantId? Sharer, ShareSessionId? Session);
    private Authorization _screenAuthorization = new(null, null);
    private readonly ScreenRtpReceiver _screenReceiver = new();
    private readonly object _videoSendGate = new();
    private readonly uint _screenSsrc = (uint)Random.Shared.NextInt64(1, uint.MaxValue);
    private ushort _screenSequence = (ushort)Random.Shared.Next(ushort.MaxValue);
    private readonly ConcurrentDictionary<uint, long> _screenSenderReports = new();
    private readonly ConcurrentDictionary<ParticipantId, long> _screenFeedbackTimes = new();
    private long _screenPacketCount, _screenOctetCount;
    private uint _screenTimestamp;

    public ParticipantId? Sharer => Volatile.Read(ref _screenAuthorization).Sharer;

    public ShareSessionId? ShareSession => Volatile.Read(ref _screenAuthorization).Session;

    public event Action<AuthorizedScreenFrame>? ScreenReceived;

    /// <summary>Authenticated RTCP receiver reports, loss fraction in 1/256 units.</summary>
    public event Action<ParticipantId, byte>? ScreenLossReport;
    public event Action<ParticipantId, TimeSpan>? ScreenRoundTrip;

    public Task RequestShareAsync() => SendSignalingAsync(new RequestShare());

    public Task RespondShareAsync(ShareRequestId request, bool accept) => SendSignalingAsync(new RespondToShareRequest(request, accept));

    public Task CancelShareAsync(ShareRequestId request) => SendSignalingAsync(new CancelShareRequest(request));

    public Task ShareStoppedAsync(ShareSessionId session) => SendSignalingAsync(new ShareStopped(session));

    /// <summary>Encode and packetize once; SRTP encrypts independently for each admitted peer.</summary>
    public int SendScreen(ShareSessionId session, ScreenFrame frame)
    {
        lock (_videoSendGate)
        {
            var permission = Volatile.Read(ref _screenAuthorization);
            if (!IsSignalingConnected || !IsAdmitted || permission.Sharer != You || permission.Session != session) { return 0; }
            var packets = ScreenRtp.Packetize(session, frame, _screenSsrc, ref _screenSequence);
            Interlocked.Add(ref _screenPacketCount, packets.Count);
            Interlocked.Add(ref _screenOctetCount, packets.Sum(p => p.Length - ScreenRtp.HeaderBytes));
            Volatile.Write(ref _screenTimestamp, frame.Timestamp);
            var targets = _audioTargets;
            var sent = 0;
            foreach (var packet in packets)
            {
                foreach (var target in targets) { if (target.Peer.SendVideoPacket(packet)) { sent += packet.Length; } }
            }
            return sent;
        }
    }

    public void SendScreenReceiverReport()
    {
        var permission = Volatile.Read(ref _screenAuthorization);
        if (permission.Sharer is not { } sharer) { return; }
        if (sharer == You) { SendScreenSenderReport(); return; }
        var target = _audioTargets.FirstOrDefault(p => p.Remote == sharer);
        if (target is not null && _screenReceiver.ReceiverReport(_screenSsrc) is { } report) { target.Peer.SendVideoPacket(report); }
    }

    private void UpdateScreenAuthorization(ParticipantId? sharer, ShareSessionId? session)
    {
        Volatile.Write(ref _screenAuthorization, new Authorization(sharer, session));
        _screenReceiver.Authorize(sharer, session);
    }

    private void OnVideoPacket(ParticipantId from, ReadOnlySpan<byte> packet)
    {
        if (!IsSignalingConnected) { return; }
        if (packet.Length == 28 && packet[1] == 200) { _screenReceiver.SenderReport(from, packet); return; }
        if (packet.Length >= 32 && packet[0] == 0x81 && packet[1] == 201 &&
            BinaryPrimitives.ReadUInt16BigEndian(packet[2..]) == 7 && BinaryPrimitives.ReadUInt32BigEndian(packet[8..]) == _screenSsrc)
        {
            if (Sharer == You && _audioTargets.Any(p => p.Remote == from))
            {
                var now = Stopwatch.GetTimestamp();
                if (_screenFeedbackTimes.TryGetValue(from, out var previous))
                {
                    if (Stopwatch.GetElapsedTime(previous) < TimeSpan.FromMilliseconds(250) || !_screenFeedbackTimes.TryUpdate(from, now, previous)) { return; }
                }
                else if (!_screenFeedbackTimes.TryAdd(from, now)) { return; }
                ScreenLossReport?.Invoke(from, packet[12]);
                var lsr = BinaryPrimitives.ReadUInt32BigEndian(packet[24..]); var delay = BinaryPrimitives.ReadUInt32BigEndian(packet[28..]);
                if (lsr != 0 && _screenSenderReports.TryGetValue(lsr, out var sent))
                {
                    var elapsed = Stopwatch.GetElapsedTime(sent) - TimeSpan.FromSeconds(delay / 65536.0);
                    if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromMinutes(1)) { ScreenRoundTrip?.Invoke(from, elapsed); }
                }
            }
            return;
        }
        if (ScreenRtp.TryHeader(packet, out var session, out _, out _, out _) && _screenReceiver.Push(from, packet) is { } frame && Sharer == from && ShareSession == session)
        { ScreenReceived?.Invoke(new AuthorizedScreenFrame(from, session, frame)); }
    }

    private void SendScreenSenderReport()
    {
        if (Interlocked.Read(ref _screenPacketCount) == 0) { return; }
        var ticks = (ulong)(DateTimeOffset.UtcNow - new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero)).Ticks;
        var seconds = (uint)(ticks / TimeSpan.TicksPerSecond); var fraction = (uint)(((ticks % TimeSpan.TicksPerSecond) << 32) / TimeSpan.TicksPerSecond);
        var report = new byte[28]; report[0] = 0x80; report[1] = 200; BinaryPrimitives.WriteUInt16BigEndian(report.AsSpan(2), 6);
        BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(4), _screenSsrc); BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(8), seconds);
        BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(12), fraction); BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(16), Volatile.Read(ref _screenTimestamp));
        BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(20), unchecked((uint)Interlocked.Read(ref _screenPacketCount)));
        BinaryPrimitives.WriteUInt32BigEndian(report.AsSpan(24), unchecked((uint)Interlocked.Read(ref _screenOctetCount)));
        _screenSenderReports[(seconds << 16) | (fraction >> 16)] = Stopwatch.GetTimestamp();
        foreach (var old in _screenSenderReports.OrderBy(p => p.Value).Take(Math.Max(0, _screenSenderReports.Count - 8))) { _screenSenderReports.TryRemove(old.Key, out _); }
        foreach (var target in _audioTargets) { target.Peer.SendVideoPacket(report); }
    }
}
