using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Coconut.Prototypes.P1.Native;

namespace Coconut.Prototypes.P1;

/// <summary>
/// Um PeerConnection por processo (é o que o P1 precisa). Os callbacks nativos chegam em threads
/// da libdatachannel; o estado compartilhado usa primitivas thread-safe.
/// </summary>
internal static unsafe class PeerSession
{
    public static readonly Stopwatch Clock = Stopwatch.StartNew();
    public static readonly ManualResetEventSlim Connected = new();
    public static readonly ManualResetEventSlim GatheringComplete = new();
    public static readonly ManualResetEventSlim DataChannelOpen = new();
    public static readonly ManualResetEventSlim TrackOpen = new();
    public static readonly ConcurrentQueue<string> ChannelMessages = new();
    public static readonly ConcurrentQueue<string> Errors = new();
    public static readonly SemaphoreSlim ChannelMessageArrived = new(0);

    private static volatile int s_dataChannel = -1;
    private static volatile int s_track = -1;

    public static int PeerConnection { get; private set; } = -1;

    public static RtcState LastState { get; private set; } = RtcState.New;

    public static double? ConnectedAtMs { get; private set; }

    public static int DataChannel => s_dataChannel;

    public static int Track => s_track;

    /// <summary>Recebe pacotes RTP da trilha remota (já decifrados do SRTP pela libdatachannel).</summary>
    public static Action<ReadOnlySpan<byte>>? OnTrackPacket { get; set; }

    public static void Create()
    {
        RtcNative.InitLogger(RtcLogLevel.Warning, &OnLog);

        // Nenhum servidor ICE: sem STUN e, principalmente, sem TURN (requisito NET-1).
        var config = new RtcConfiguration { IceServersCount = 0, DisableAutoNegotiation = 1 };
        PeerConnection = RtcNative.CreatePeerConnection(&config);
        RtcNative.Check(PeerConnection, "rtcCreatePeerConnection");
        RtcNative.Check(RtcNative.SetStateChangeCallback(PeerConnection, &OnState), "rtcSetStateChangeCallback");
        RtcNative.Check(RtcNative.SetGatheringStateChangeCallback(PeerConnection, &OnGathering), "rtcSetGatheringStateChangeCallback");
        RtcNative.Check(RtcNative.SetDataChannelCallback(PeerConnection, &OnDataChannel), "rtcSetDataChannelCallback");
        RtcNative.Check(RtcNative.SetTrackCallback(PeerConnection, &OnTrack), "rtcSetTrackCallback");
    }

    public static void UseDataChannel(int dc)
    {
        s_dataChannel = dc;
        RtcNative.Check(RtcNative.SetOpenCallback(dc, &OnDataChannelOpen), "rtcSetOpenCallback(dc)");
        RtcNative.Check(RtcNative.SetMessageCallback(dc, &OnChannelMessage), "rtcSetMessageCallback(dc)");
    }

    public static void UseTrack(int tr)
    {
        s_track = tr;
        RtcNative.Check(RtcNative.SetOpenCallback(tr, &OnTrackOpen), "rtcSetOpenCallback(track)");
        RtcNative.Check(RtcNative.SetMessageCallback(tr, &OnTrackMessage), "rtcSetMessageCallback(track)");
    }

    public static string LocalDescription() =>
        RtcNative.ReadString((p, n) => RtcNative.GetLocalDescription(PeerConnection, (byte*)p, n));

    /// <summary>Tipos dos candidatos selecionados (host/srflx/prflx/relay), sem endereços.</summary>
    public static (string Local, string Remote) SelectedCandidateTypes()
    {
        var local = new byte[512];
        var remote = new byte[512];
        fixed (byte* l = local)
        fixed (byte* r = remote)
        {
            if (RtcNative.GetSelectedCandidatePair(PeerConnection, l, local.Length, r, remote.Length) < 0)
            {
                return ("desconhecido", "desconhecido");
            }
        }

        return (Sdp.CandidateType(Encoding.UTF8.GetString(local).TrimEnd('\0')),
                Sdp.CandidateType(Encoding.UTF8.GetString(remote).TrimEnd('\0')));
    }

    public static void SendText(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + '\0');
        fixed (byte* p = bytes)
        {
            // Tamanho negativo: a libdatachannel envia como mensagem de texto terminada em NUL.
            RtcNative.Check(RtcNative.SendMessage(DataChannel, p, -1), "rtcSendMessage(dc)");
        }
    }

    public static string? WaitChannelMessage(TimeSpan timeout)
    {
        return ChannelMessageArrived.Wait(timeout) && ChannelMessages.TryDequeue(out var m) ? m : null;
    }

    public static void Close()
    {
        if (PeerConnection >= 0)
        {
            if (RtcNative.ClosePeerConnection(PeerConnection) < 0 | RtcNative.DeletePeerConnection(PeerConnection) < 0)
            {
                Errors.Enqueue("falha ao encerrar o PeerConnection");
            }
        }

        RtcNative.Cleanup();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnLog(RtcLogLevel level, byte* message) =>
        Console.Error.WriteLine($"[rtc {level}] {Marshal.PtrToStringUTF8((IntPtr)message)}");

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnState(int pc, RtcState state, void* ptr)
    {
        LastState = state;
        if (state == RtcState.Connected)
        {
            ConnectedAtMs = Clock.Elapsed.TotalMilliseconds;
            Connected.Set();
        }
        else if (state is RtcState.Failed or RtcState.Disconnected)
        {
            Errors.Enqueue($"estado {state}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGathering(int pc, RtcGatheringState state, void* ptr)
    {
        if (state == RtcGatheringState.Complete)
        {
            GatheringComplete.Set();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDataChannel(int pc, int dc, void* ptr) => Guarded(() => UseDataChannel(dc));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnTrack(int pc, int tr, void* ptr) => Guarded(() => UseTrack(tr));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDataChannelOpen(int id, void* ptr) => DataChannelOpen.Set();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnTrackOpen(int id, void* ptr) => TrackOpen.Set();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnChannelMessage(int id, byte* message, int size, void* ptr)
    {
        var text = size < 0
            ? Marshal.PtrToStringUTF8((IntPtr)message) ?? string.Empty
            : Encoding.UTF8.GetString(message, size);
        ChannelMessages.Enqueue(text);
        ChannelMessageArrived.Release();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnTrackMessage(int id, byte* message, int size, void* ptr)
    {
        if (size <= 0 || OnTrackPacket is not { } handler)
        {
            return;
        }

        var packet = new ReadOnlySpan<byte>(message, size);
        try
        {
            handler(packet);
        }
#pragma warning disable CA1031 // Exceção não pode atravessar a fronteira nativa; é registrada como falha do protótipo.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Errors.Enqueue($"trilha: {ex.Message}");
        }
    }

    private static void Guarded(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // Idem: callbacks nativos não podem lançar.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Errors.Enqueue(ex.Message);
        }
    }
}
