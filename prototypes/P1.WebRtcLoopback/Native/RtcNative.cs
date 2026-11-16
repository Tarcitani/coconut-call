using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]

namespace Coconut.Prototypes.P1.Native;

// Espelho de include/rtc/rtc.h da libdatachannel v0.24.6 (somente o necessário ao P1).
// bool do C = 1 byte → byte aqui, para manter as estruturas blittable com o layout x64 do MSVC.

internal enum RtcState
{
    New = 0,
    Connecting = 1,
    Connected = 2,
    Disconnected = 3,
    Failed = 4,
    Closed = 5,
}

internal enum RtcGatheringState
{
    New = 0,
    InProgress = 1,
    Complete = 2,
}

internal enum RtcDirection
{
    Unknown = 0,
    SendOnly = 1,
    RecvOnly = 2,
    SendRecv = 3,
    Inactive = 4,
}

internal enum RtcCodec
{
    H264 = 0,
    Opus = 128,
}

internal enum RtcLogLevel
{
    None = 0,
    Fatal = 1,
    Error = 2,
    Warning = 3,
    Info = 4,
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RtcConfiguration
{
    public byte** IceServers;
    public int IceServersCount;
    public byte* ProxyServer;
    public byte* BindAddress;
    public int CertificateType;
    public int IceTransportPolicy;
    public byte EnableIceTcp;
    public byte EnableIceUdpMux;
    public byte DisableAutoNegotiation;
    public byte ForceMediaTransport;
    public ushort PortRangeBegin;
    public ushort PortRangeEnd;
    public int Mtu;
    public int MaxMessageSize;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RtcTrackInit
{
    public RtcDirection Direction;
    public RtcCodec Codec;
    public int PayloadType;
    public uint Ssrc;
    public byte* Mid;
    public byte* Name;
    public byte* Msid;
    public byte* TrackId;
    public byte* Profile;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct RtcPacketizerInit
{
    public uint Ssrc;
    public byte* Cname;
    public byte PayloadType;
    public uint ClockRate;
    public ushort SequenceNumber;
    public uint Timestamp;
    public ushort MaxFragmentSize;
    public int NalSeparator;
    public int ObuPacketization;
    public byte PlayoutDelayId;
    public ushort PlayoutDelayMin;
    public ushort PlayoutDelayMax;
    public byte ColorSpaceId;
    public byte ColorChromaSitingHorz;
    public byte ColorChromaSitingVert;
    public byte ColorRange;
    public byte ColorPrimaries;
    public byte ColorTransfer;
    public byte ColorMatrix;
}

internal static unsafe partial class RtcNative
{
    private const string Lib = "datachannel";

    [LibraryImport(Lib, EntryPoint = "rtcInitLogger")]
    public static partial void InitLogger(RtcLogLevel level, delegate* unmanaged[Cdecl]<RtcLogLevel, byte*, void> callback);

    [LibraryImport(Lib, EntryPoint = "rtcCreatePeerConnection")]
    public static partial int CreatePeerConnection(RtcConfiguration* config);

    [LibraryImport(Lib, EntryPoint = "rtcClosePeerConnection")]
    public static partial int ClosePeerConnection(int pc);

    [LibraryImport(Lib, EntryPoint = "rtcDeletePeerConnection")]
    public static partial int DeletePeerConnection(int pc);

    [LibraryImport(Lib, EntryPoint = "rtcSetStateChangeCallback")]
    public static partial int SetStateChangeCallback(int pc, delegate* unmanaged[Cdecl]<int, RtcState, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSetGatheringStateChangeCallback")]
    public static partial int SetGatheringStateChangeCallback(int pc, delegate* unmanaged[Cdecl]<int, RtcGatheringState, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSetDataChannelCallback")]
    public static partial int SetDataChannelCallback(int pc, delegate* unmanaged[Cdecl]<int, int, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSetTrackCallback")]
    public static partial int SetTrackCallback(int pc, delegate* unmanaged[Cdecl]<int, int, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSetLocalDescription", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetLocalDescription(int pc, string? type);

    [LibraryImport(Lib, EntryPoint = "rtcSetRemoteDescription", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetRemoteDescription(int pc, string sdp, string type);

    [LibraryImport(Lib, EntryPoint = "rtcGetLocalDescription")]
    public static partial int GetLocalDescription(int pc, byte* buffer, int size);

    [LibraryImport(Lib, EntryPoint = "rtcGetSelectedCandidatePair")]
    public static partial int GetSelectedCandidatePair(int pc, byte* local, int localSize, byte* remote, int remoteSize);

    [LibraryImport(Lib, EntryPoint = "rtcCreateDataChannel", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int CreateDataChannel(int pc, string label);

    [LibraryImport(Lib, EntryPoint = "rtcAddTrackEx")]
    public static partial int AddTrackEx(int pc, RtcTrackInit* init);

    [LibraryImport(Lib, EntryPoint = "rtcSetOpusPacketizer")]
    public static partial int SetOpusPacketizer(int tr, RtcPacketizerInit* init);

    [LibraryImport(Lib, EntryPoint = "rtcChainRtcpSrReporter")]
    public static partial int ChainRtcpSrReporter(int tr);

    [LibraryImport(Lib, EntryPoint = "rtcSetTrackRtpTimestamp")]
    public static partial int SetTrackRtpTimestamp(int tr, uint timestamp);

    [LibraryImport(Lib, EntryPoint = "rtcSetOpenCallback")]
    public static partial int SetOpenCallback(int id, delegate* unmanaged[Cdecl]<int, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSetMessageCallback")]
    public static partial int SetMessageCallback(int id, delegate* unmanaged[Cdecl]<int, byte*, int, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSendMessage")]
    public static partial int SendMessage(int id, byte* data, int size);

    [LibraryImport(Lib, EntryPoint = "rtcCleanup")]
    public static partial void Cleanup();

    public static string ReadString(Func<IntPtr, int, int> getter)
    {
        var buffer = new byte[16 * 1024];
        fixed (byte* p = buffer)
        {
            var size = getter((IntPtr)p, buffer.Length);
            if (size < 0)
            {
                throw new InvalidOperationException($"libdatachannel retornou {size}");
            }

            return System.Text.Encoding.UTF8.GetString(buffer, 0, Math.Max(0, size - 1));
        }
    }

    public static void Check(int result, string operation)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"{operation} falhou com código {result}");
        }
    }
}
