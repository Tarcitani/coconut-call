using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Coconut.Screen;

public sealed record ScreenMonitor(nint Handle, string Name, int Width, int Height, bool IsPrimary);

public sealed record ScreenOptions(int MaxHeight = 720, int FramesPerSecond = 30, int Bitrate = 2_000_000, bool ForceSoftware = false)
{
    public void Validate()
    {
        if (MaxHeight is not (480 or 720 or 1080) || FramesPerSecond is not (15 or 30 or 60) || Bitrate is < 256_000 or > 12_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHeight), "Escolha 480/720/1080p, 15/30/60 fps e 256–12000 kbps.");
        }
    }

    public (int Width, int Height) Dimensions(ScreenMonitor monitor)
    {
        var scale = Math.Min(1, Math.Min(MaxHeight / (double)monitor.Height, (MaxHeight * 16.0 / 9) / monitor.Width));
        return (Math.Max(160, (int)(monitor.Width * scale) / 2 * 2), Math.Max(90, (int)(monitor.Height * scale) / 2 * 2));
    }
}

public delegate void EncodedFrameHandler(ReadOnlySpan<byte> sample, long timestamp, bool keyframe, ReadOnlySpan<byte> sequenceHeader);

/// <summary>Captura explícita de um monitor, com fila nativa de um quadro e codificação única.</summary>
public sealed unsafe class ScreenCapture : IDisposable
{
    private static readonly ConcurrentDictionary<nint, ScreenCapture> s_live = new();
    private static long s_nextKey;
    private readonly nint _key = (nint)Interlocked.Increment(ref s_nextKey);
    private readonly object _gate = new();
    private nint _handle;

    private ScreenCapture() { }

    public event EncodedFrameHandler? Frame;

    public event Action<int>? Faulted;

    public bool IsHardware { get; private set; }

    public int Width { get; private set; }

    public int Height { get; private set; }
    public bool NeedsReconfigure { get { lock (_gate) { return _handle != 0 && ScreenNative.CaptureReconfigure(_handle) != 0; } } }

    public static ScreenCapture Start(ScreenMonitor monitor, ScreenOptions options, EncodedFrameHandler onFrame, Action<int> onError)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var capture = new ScreenCapture { Frame = onFrame, Faulted = onError };
        (capture.Width, capture.Height) = options.Dimensions(monitor);
        s_live[capture._key] = capture;
        try
        {
            Marshal.ThrowExceptionForHR(ScreenNative.CreateCapture(monitor.Handle, capture.Width, capture.Height,
                options.FramesPerSecond, options.Bitrate, options.ForceSoftware ? 1 : 0, &OnFrame, &OnError, capture._key, out capture._handle));
            capture.IsHardware = ScreenNative.CaptureHardware(capture._handle) != 0;
            return capture;
        }
        catch
        {
            s_live.TryRemove(capture._key, out _);
            throw;
        }
    }

    public void SetBitrate(int bitrate)
    {
        lock (_gate) { if (_handle != 0) { ScreenNative.CaptureBitrate(_handle, bitrate); } }
    }

    public void RequestKeyframe()
    {
        lock (_gate) { if (_handle != 0) { ScreenNative.CaptureKeyframe(_handle); } }
    }

    public void Dispose()
    {
        // Remove callbacks first; native destruction joins its worker before the handle can be reused.
        s_live.TryRemove(_key, out _);
        lock (_gate)
        {
            if (_handle != 0) { ScreenNative.DestroyCapture(_handle); _handle = 0; }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnFrame(nint key, byte* data, int size, long time, int keyframe, byte* header, int headerSize)
    {
        if (!s_live.TryGetValue(key, out var capture)) { return; }
        try { capture.Frame?.Invoke(new ReadOnlySpan<byte>(data, size), time, keyframe != 0, new ReadOnlySpan<byte>(header, headerSize)); }
#pragma warning disable CA1031 // Nothing may escape a native callback.
        catch { capture.Faulted?.Invoke(unchecked((int)0x80004005)); }
#pragma warning restore CA1031
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnError(nint key, int code)
    {
        if (s_live.TryGetValue(key, out var capture)) { capture.Faulted?.Invoke(code); }
    }
}

/// <summary>Decoder MF com saída D3D11. Attach deve ser chamado na thread de UI com o IUnknown WinUI real.</summary>
public sealed class ScreenRenderer : IDisposable
{
    public static void Clear(nint panel) => Marshal.ThrowExceptionForHR(ScreenNative.ClearPanel(panel));
    private readonly object _gate = new();
    private nint _handle;

    public ScreenRenderer(int width, int height) => Marshal.ThrowExceptionForHR(ScreenNative.CreateRenderer(width, height, out _handle));

    public void Attach(nint panel)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_handle == 0, this); Marshal.ThrowExceptionForHR(ScreenNative.AttachRenderer(_handle, panel)); }
    }

    public unsafe void Push(ReadOnlySpan<byte> encoded, long timestamp)
    {
        lock (_gate)
        {
            if (_handle == 0) { return; }
            fixed (byte* data = encoded) { Marshal.ThrowExceptionForHR(ScreenNative.PushRenderer(_handle, data, encoded.Length, timestamp)); }
        }
    }

    public ulong Frames { get { lock (_gate) { return _handle == 0 ? 0 : ScreenNative.RendererFrames(_handle); } } }

    public void Dispose()
    {
        lock (_gate) { if (_handle != 0) { ScreenNative.DestroyRenderer(_handle); _handle = 0; } }
    }
}

internal static unsafe partial class ScreenNative
{
    private const string Lib = "Coconut.Screen.Native.dll";
    [LibraryImport(Lib, EntryPoint = "coconut_capture_create")]
    internal static partial int CreateCapture(nint monitor, int width, int height, int fps, int bitrate, int software,
        delegate* unmanaged[Cdecl]<nint, byte*, int, long, int, byte*, int, void> frame, delegate* unmanaged[Cdecl]<nint, int, void> error, nint key, out nint handle);
    [LibraryImport(Lib, EntryPoint = "coconut_capture_destroy")]
    internal static partial void DestroyCapture(nint capture);
    [LibraryImport(Lib, EntryPoint = "coconut_capture_hardware")]
    internal static partial int CaptureHardware(nint capture);
    [LibraryImport(Lib, EntryPoint = "coconut_capture_reconfigure")]
    internal static partial int CaptureReconfigure(nint capture);
    [LibraryImport(Lib, EntryPoint = "coconut_capture_bitrate")]
    internal static partial void CaptureBitrate(nint capture, int bitrate);
    [LibraryImport(Lib, EntryPoint = "coconut_capture_keyframe")]
    internal static partial void CaptureKeyframe(nint capture);
    [LibraryImport(Lib, EntryPoint = "coconut_renderer_create")]
    internal static partial int CreateRenderer(int width, int height, out nint renderer);
    [LibraryImport(Lib, EntryPoint = "coconut_renderer_attach")]
    internal static partial int AttachRenderer(nint renderer, nint panel);
    [LibraryImport(Lib, EntryPoint = "coconut_panel_clear")]
    internal static partial int ClearPanel(nint panel);
    [LibraryImport(Lib, EntryPoint = "coconut_renderer_push")]
    internal static partial int PushRenderer(nint renderer, byte* encoded, int size, long time);
    [LibraryImport(Lib, EntryPoint = "coconut_renderer_frames")]
    internal static partial ulong RendererFrames(nint renderer);
    [LibraryImport(Lib, EntryPoint = "coconut_renderer_destroy")]
    internal static partial void DestroyRenderer(nint renderer);
}
