using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Coconut.Media.Native.Interop;

namespace Coconut.Media.Native;

public delegate void RtpPacketHandler(ReadOnlySpan<byte> packet);

/// <summary>Opções de uma conexão P2P. Somente STUN: TURN/relay é proibido pelo produto (NET-1).</summary>
public sealed record RtcPeerOptions
{
    public IReadOnlyList<string> StunServers { get; init; } = [];

    public ushort PortRangeBegin { get; init; }

    public ushort PortRangeEnd { get; init; }
}

/// <summary>
/// Uma conexão WebRTC com um par: um canal de dados e uma trilha de áudio Opus (sendrecv, mid "audio").
/// Callbacks chegam em threads da libdatachannel; consumidores devem repassar para seu próprio laço.
/// </summary>
public sealed unsafe class RtcPeer : IDisposable
{
    public const byte OpusPayloadType = 111;
    public const string AudioMid = "audio";
    private static readonly ConcurrentDictionary<nint, RtcPeer> s_live = new();
    private static long s_nextKey;
    private static int s_loggerInitialized;
    private static bool s_nativeLogToStderr;

    private readonly int _pc;
    // Chave do registro em vez de GCHandle: callback tardio após Dispose encontra nada e retorna.
    private readonly nint _key = (nint)Interlocked.Increment(ref s_nextKey);
    private volatile int _dataChannel = -1;
    private volatile int _audioTrack = -1;
    private volatile int _videoTrack = -1;
    private int _disposed;

    public RtcPeer(RtcPeerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.StunServers.Any(s => !s.StartsWith("stun:", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Somente servidores stun: são permitidos; TURN/relay é proibido.", nameof(options));
        }

        if (Interlocked.Exchange(ref s_loggerInitialized, 1) == 0)
        {
            // Diagnóstico local apenas: COCONUT_NATIVE_LOG=1 escreve logs nativos (que podem conter endereços) no stderr.
            s_nativeLogToStderr = Environment.GetEnvironmentVariable("COCONUT_NATIVE_LOG") == "1";
            RtcNative.InitLogger(s_nativeLogToStderr ? RtcLogLevel.Warning : RtcLogLevel.Error, &OnLog);
        }

        s_live[_key] = this;
        var servers = options.StunServers.Select(s => Marshal.StringToCoTaskMemUTF8(s)).ToArray();
        try
        {
            fixed (IntPtr* serverPointers = servers)
            {
                var config = new RtcConfiguration
                {
                    IceServers = (byte**)serverPointers,
                    IceServersCount = servers.Length,
                    DisableAutoNegotiation = 1,
                    PortRangeBegin = options.PortRangeBegin,
                    PortRangeEnd = options.PortRangeEnd,
                };
                _pc = RtcNative.CreatePeerConnection(&config);
            }
        }
        finally
        {
            foreach (var s in servers)
            {
                Marshal.FreeCoTaskMem(s);
            }
        }

        RtcNative.Check(_pc, "rtcCreatePeerConnection");
        RtcNative.SetUserPointer(_pc, (void*)_key);
        RtcNative.Check(RtcNative.SetLocalDescriptionCallback(_pc, &OnLocalDescription), "rtcSetLocalDescriptionCallback");
        RtcNative.Check(RtcNative.SetLocalCandidateCallback(_pc, &OnLocalCandidate), "rtcSetLocalCandidateCallback");
        RtcNative.Check(RtcNative.SetStateChangeCallback(_pc, &OnState), "rtcSetStateChangeCallback");
        RtcNative.Check(RtcNative.SetDataChannelCallback(_pc, &OnRemoteDataChannel), "rtcSetDataChannelCallback");
    }

    public event Action<string, string>? LocalDescriptionReady;  // (type, sdp)

    public event Action<string, string?>? LocalCandidateReady;    // (candidate, mid)

    public event Action<RtcState>? StateChanged;

    public event Action? DataChannelOpened;

    public event Action<string>? DataMessageReceived;

    public event RtpPacketHandler? AudioPacketReceived;

    public event RtpPacketHandler? VideoPacketReceived;

    public bool IsVideoOpen => _videoTrack >= 0 && RtcNative.IsOpen(_videoTrack);

    /// <summary>Exceção lançada por um assinante dentro de um callback nativo (não pode atravessar a fronteira).</summary>
    public event Action<Exception>? CallbackFaulted;

    public RtcState State { get; private set; } = RtcState.New;

    public bool IsDataChannelOpen => _dataChannel >= 0 && RtcNative.IsOpen(_dataChannel);

    public bool IsAudioOpen => _audioTrack >= 0 && RtcNative.IsOpen(_audioTrack);

    /// <summary>Deve ser chamado antes de qualquer descrição (ofertante e respondente), para que o mid case.</summary>
    public void AddAudioTrack(uint ssrc, string cname)
    {
        var mid = Marshal.StringToCoTaskMemUTF8(AudioMid);
        var name = Marshal.StringToCoTaskMemUTF8(cname);
        try
        {
            var init = new RtcTrackInit
            {
                Direction = RtcDirection.SendRecv,
                Codec = RtcCodec.Opus,
                PayloadType = OpusPayloadType,
                Ssrc = ssrc,
                Mid = (byte*)mid,
                Msid = (byte*)name,
                TrackId = (byte*)mid,
            };
            var track = RtcNative.AddTrackEx(_pc, &init);
            RtcNative.Check(track, "rtcAddTrackEx");

            var packetizer = new RtcPacketizerInit
            {
                Ssrc = ssrc,
                Cname = (byte*)name,
                PayloadType = OpusPayloadType,
                ClockRate = OpusCodec.SampleRate,
                SequenceNumber = (ushort)Random.Shared.Next(ushort.MaxValue),
                Timestamp = (uint)Random.Shared.NextInt64(uint.MaxValue),
            };
            RtcNative.Check(RtcNative.SetOpusPacketizer(track, &packetizer), "rtcSetOpusPacketizer");
            RtcNative.Check(RtcNative.ChainRtcpSrReporter(track), "rtcChainRtcpSrReporter");
            RtcNative.SetUserPointer(track, (void*)_key);
            RtcNative.Check(RtcNative.SetMessageCallback(track, &OnAudioMessage), "rtcSetMessageCallback(track)");
            _audioTrack = track;
        }
        finally
        {
            Marshal.FreeCoTaskMem(mid);
            Marshal.FreeCoTaskMem(name);
        }
    }

    /// <summary>Raw RTP/RTCP track. Managed RFC 6184 packetization includes the authorized session extension.</summary>
    public void AddVideoTrack(uint ssrc)
    {
        var mid = Marshal.StringToCoTaskMemUTF8("screen");
        var profile = Marshal.StringToCoTaskMemUTF8("42e02a");
        try
        {
            var init = new RtcTrackInit { Direction = RtcDirection.SendRecv, Codec = RtcCodec.H264,
                PayloadType = 96, Ssrc = ssrc, Mid = (byte*)mid, Msid = (byte*)mid, TrackId = (byte*)mid, Profile = (byte*)profile };
            var track = RtcNative.AddTrackEx(_pc, &init); RtcNative.Check(track, "rtcAddTrackEx(video)");
            RtcNative.SetUserPointer(track, (void*)_key);
            RtcNative.Check(RtcNative.SetMessageCallback(track, &OnVideoMessage), "rtcSetMessageCallback(video)");
            _videoTrack = track;
        }
        finally { Marshal.FreeCoTaskMem(mid); Marshal.FreeCoTaskMem(profile); }
    }

    public bool SendVideoPacket(ReadOnlySpan<byte> packet)
    {
        var track = _videoTrack;
        if (track < 0 || !RtcNative.IsOpen(track) || RtcNative.GetBufferedAmount(track) > 256 * 1024) { return false; }
        fixed (byte* data = packet) { return RtcNative.SendMessage(track, data, packet.Length) >= 0; }
    }

    /// <summary>Só o ofertante cria o canal; o respondente o recebe da negociação.</summary>
    public void CreateDataChannel(string label)
    {
        var dc = RtcNative.CreateDataChannel(_pc, label);
        RtcNative.Check(dc, "rtcCreateDataChannel");
        AdoptDataChannel(dc);
    }

    public void SetLocalDescription(string? type) =>
        RtcNative.Check(RtcNative.SetLocalDescription(_pc, type), "rtcSetLocalDescription");

    public void SetRemoteDescription(string sdp, string type) =>
        RtcNative.Check(RtcNative.SetRemoteDescription(_pc, sdp, type), "rtcSetRemoteDescription");

    public void AddRemoteCandidate(string candidate, string? mid) =>
        RtcNative.Check(RtcNative.AddRemoteCandidate(_pc, candidate, mid), "rtcAddRemoteCandidate");

    public bool SendText(string text)
    {
        if (_dataChannel < 0)
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(text + '\0');
        fixed (byte* p = bytes)
        {
            return RtcNative.SendMessage(_dataChannel, p, -1) >= 0; // tamanho negativo = texto terminado em NUL
        }
    }

    /// <summary>Envia um quadro Opus já codificado (codificado uma vez e repassado a cada par).</summary>
    public bool SendAudioFrame(ReadOnlySpan<byte> opusFrame, uint rtpTimestamp)
    {
        var track = _audioTrack;
        if (track < 0 || !RtcNative.IsOpen(track) || RtcNative.SetTrackRtpTimestamp(track, rtpTimestamp) < 0)
        {
            return false;
        }

        fixed (byte* p = opusFrame)
        {
            return RtcNative.SendMessage(track, p, opusFrame.Length) >= 0;
        }
    }

    /// <summary>Tipos dos candidatos do par selecionado (host/srflx/prflx/relay), nunca endereços.</summary>
    public (string Local, string Remote) SelectedCandidateTypes()
    {
        Span<byte> local = stackalloc byte[512];
        Span<byte> remote = stackalloc byte[512];
        fixed (byte* l = local)
        fixed (byte* r = remote)
        {
            if (RtcNative.GetSelectedCandidatePair(_pc, l, local.Length, r, remote.Length) < 0)
            {
                return ("unknown", "unknown");
            }
        }

        return (CandidateType(local), CandidateType(remote));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        s_live.TryRemove(_key, out _);
        if (_dataChannel >= 0)
        {
            _ = RtcNative.Delete(_dataChannel);
        }

        if (_audioTrack >= 0)
        {
            _ = RtcNative.Delete(_audioTrack);
        }

        if (_videoTrack >= 0) { _ = RtcNative.Delete(_videoTrack); }

        _ = RtcNative.DeletePeerConnection(_pc);
    }

    private static string CandidateType(ReadOnlySpan<byte> candidate)
    {
        var text = Encoding.UTF8.GetString(candidate).TrimEnd('\0');
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.IndexOf(parts, "typ");
        return index >= 0 && index + 1 < parts.Length ? parts[index + 1] : "unknown";
    }

    private void AdoptDataChannel(int dc)
    {
        RtcNative.SetUserPointer(dc, (void*)_key);
        RtcNative.Check(RtcNative.SetOpenCallback(dc, &OnDataChannelOpen), "rtcSetOpenCallback(dc)");
        RtcNative.Check(RtcNative.SetMessageCallback(dc, &OnDataMessage), "rtcSetMessageCallback(dc)");
        _dataChannel = dc;
        if (RtcNative.IsOpen(dc))
        {
            DataChannelOpened?.Invoke();
        }
    }

    private static void Dispatch(void* ptr, Action<RtcPeer> action)
    {
        if (!s_live.TryGetValue((nint)ptr, out var peer))
        {
            return;
        }

        try
        {
            action(peer);
        }
#pragma warning disable CA1031 // Exceções não podem atravessar a fronteira nativa; são entregues ao assinante.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            peer.CallbackFaulted?.Invoke(ex);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnLog(RtcLogLevel level, byte* message)
    {
        // Logs nativos podem conter endereços; nunca vão ao registro do aplicativo.
        if (s_nativeLogToStderr)
        {
            Console.Error.WriteLine($"[rtc {level}] {Marshal.PtrToStringUTF8((IntPtr)message)}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnLocalDescription(int pc, byte* sdp, byte* type, void* ptr)
    {
        var sdpText = Marshal.PtrToStringUTF8((IntPtr)sdp) ?? string.Empty;
        var typeText = Marshal.PtrToStringUTF8((IntPtr)type) ?? string.Empty;
        Dispatch(ptr, p => p.LocalDescriptionReady?.Invoke(typeText, sdpText));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnLocalCandidate(int pc, byte* candidate, byte* mid, void* ptr)
    {
        var candidateText = Marshal.PtrToStringUTF8((IntPtr)candidate) ?? string.Empty;
        var midText = Marshal.PtrToStringUTF8((IntPtr)mid);
        Dispatch(ptr, p => p.LocalCandidateReady?.Invoke(candidateText, midText));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnState(int pc, RtcState state, void* ptr) => Dispatch(ptr, p =>
    {
        p.State = state;
        p.StateChanged?.Invoke(state);
    });

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnRemoteDataChannel(int pc, int dc, void* ptr) => Dispatch(ptr, p => p.AdoptDataChannel(dc));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDataChannelOpen(int id, void* ptr) => Dispatch(ptr, p => p.DataChannelOpened?.Invoke());

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDataMessage(int id, byte* message, int size, void* ptr)
    {
        var text = size < 0 ? Marshal.PtrToStringUTF8((IntPtr)message) ?? string.Empty : Encoding.UTF8.GetString(message, size);
        Dispatch(ptr, p => p.DataMessageReceived?.Invoke(text));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAudioMessage(int id, byte* message, int size, void* ptr)
    {
        if (size <= 0 || !s_live.TryGetValue((nint)ptr, out var peer))
        {
            return;
        }

        try
        {
            peer.AudioPacketReceived?.Invoke(new ReadOnlySpan<byte>(message, size));
        }
#pragma warning disable CA1031 // Idem.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            peer.CallbackFaulted?.Invoke(ex);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnVideoMessage(int id, byte* message, int size, void* ptr)
    {
        if (size <= 0 || !s_live.TryGetValue((nint)ptr, out var peer)) { return; }
        try { peer.VideoPacketReceived?.Invoke(new ReadOnlySpan<byte>(message, size)); }
#pragma warning disable CA1031 // Native callbacks must never unwind into C++.
        catch (Exception ex) { peer.CallbackFaulted?.Invoke(ex); }
#pragma warning restore CA1031
    }
}
