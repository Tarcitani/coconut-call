using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]

namespace Coconut.Media.Native.Interop;

// Espelho de include/rtc/rtc.h da libdatachannel v0.24.6 (SHA-256 ad1e7ed7…a520d), não da documentação
// do branch principal. bool do C = 1 byte → byte, mantendo as estruturas blittable no layout x64 do MSVC.

public enum RtcState
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

    [LibraryImport(Lib, EntryPoint = "rtcSetUserPointer")]
    public static partial void SetUserPointer(int id, void* ptr);

    [LibraryImport(Lib, EntryPoint = "rtcCreatePeerConnection")]
    public static partial int CreatePeerConnection(RtcConfiguration* config);

    [LibraryImport(Lib, EntryPoint = "rtcDeletePeerConnection")]
    public static partial int DeletePeerConnection(int pc);

    [LibraryImport(Lib, EntryPoint = "rtcSetLocalDescriptionCallback")]
    public static partial int SetLocalDescriptionCallback(int pc, delegate* unmanaged[Cdecl]<int, byte*, byte*, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSetLocalCandidateCallback")]
    public static partial int SetLocalCandidateCallback(int pc, delegate* unmanaged[Cdecl]<int, byte*, byte*, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSetStateChangeCallback")]
    public static partial int SetStateChangeCallback(int pc, delegate* unmanaged[Cdecl]<int, RtcState, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSetDataChannelCallback")]
    public static partial int SetDataChannelCallback(int pc, delegate* unmanaged[Cdecl]<int, int, void*, void> cb);

    [LibraryImport(Lib, EntryPoint = "rtcSetLocalDescription", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetLocalDescription(int pc, string? type);

    [LibraryImport(Lib, EntryPoint = "rtcSetRemoteDescription", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetRemoteDescription(int pc, string sdp, string type);

    [LibraryImport(Lib, EntryPoint = "rtcAddRemoteCandidate", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int AddRemoteCandidate(int pc, string candidate, string? mid);

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

    [LibraryImport(Lib, EntryPoint = "rtcIsOpen")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool IsOpen(int id);

    [LibraryImport(Lib, EntryPoint = "rtcDelete")]
    public static partial int Delete(int id);

    public static void Check(int result, string operation)
    {
        if (result < 0)
        {
            throw new RtcException($"{operation} falhou com código {result}");
        }
    }
}

public sealed class RtcException : Exception
{
    public RtcException()
    {
    }

    public RtcException(string message)
        : base(message)
    {
    }

    public RtcException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
