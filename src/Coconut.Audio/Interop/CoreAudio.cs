using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Coconut.Audio.Interop;

// Transcrito de mmdeviceapi.h e audioclient.h do Windows SDK 10.0.26100.0 (IIDs e ordem de métodos
// conferidos no cabeçalho — a página de IAudioEffectsManager lista os métodos em ordem alfabética, não na da vtable).

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WaveFormatEx
{
    public ushort FormatTag;
    public ushort Channels;
    public uint SamplesPerSec;
    public uint AvgBytesPerSec;
    public ushort BlockAlign;
    public ushort BitsPerSample;
    public ushort ExtraSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioClientProperties
{
    public uint Size;
    public int IsOffload;
    public int Category;
    public int Options;
}

[StructLayout(LayoutKind.Sequential)]
internal struct AudioEffectNative
{
    public Guid Id;
    public int CanSetState;
    public int State;
}

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    IMMDeviceCollection EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? device);

    IMMDevice GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id);

    void RegisterEndpointNotificationCallback(IMMNotificationClient client);

    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    uint GetCount();

    IMMDevice Item(uint index);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    nint Activate(in Guid iid, uint clsContext, nint activationParams);

    IPropertyStore OpenPropertyStore(uint access);

    nint GetId(); // LPWSTR alocada pelo COM; o chamador libera com CoTaskMemFree

    uint GetState();
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal unsafe partial interface IPropertyStore
{
    uint GetCount();

    void GetAt(uint index, out PropertyKey key);

    void GetValue(in PropertyKey key, byte* propVariant);
}

[GeneratedComInterface]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
internal partial interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

[GeneratedComInterface]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
internal unsafe partial interface IAudioClient
{
    void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, WaveFormatEx* format, Guid* sessionGuid);

    uint GetBufferSize();

    long GetStreamLatency();

    uint GetCurrentPadding();

    [PreserveSig]
    int IsFormatSupported(int shareMode, WaveFormatEx* format, WaveFormatEx** closestMatch);

    WaveFormatEx* GetMixFormat();

    void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    void Start();

    void Stop();

    void Reset();

    void SetEventHandle(nint eventHandle);

    [PreserveSig]
    int GetService(in Guid iid, out nint service);
}

[GeneratedComInterface]
[Guid("726778CD-F60A-4eda-82DE-E47610CD78AA")]
internal unsafe partial interface IAudioClient2 : IAudioClient
{
    int IsOffloadCapable(int category);

    void SetClientProperties(AudioClientProperties* properties);

    void GetBufferSizeLimits(WaveFormatEx* format, int eventDriven, out long minBufferDuration, out long maxBufferDuration);
}

[GeneratedComInterface]
[Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
internal unsafe partial interface IAudioCaptureClient
{
    [PreserveSig]
    int GetBuffer(byte** data, uint* framesToRead, uint* flags, ulong* devicePosition, ulong* qpcPosition);

    void ReleaseBuffer(uint framesRead);

    [PreserveSig]
    int GetNextPacketSize(uint* framesInNextPacket);
}

[GeneratedComInterface]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
internal unsafe partial interface IAudioRenderClient
{
    [PreserveSig]
    int GetBuffer(uint framesRequested, byte** data);

    void ReleaseBuffer(uint framesWritten, uint flags);
}

[GeneratedComInterface]
[Guid("4460B3AE-4B44-4527-8676-7548A8ACD260")]
internal unsafe partial interface IAudioEffectsManager
{
    void RegisterAudioEffectsChangedNotificationCallback(nint client);

    void UnregisterAudioEffectsChangedNotificationCallback(nint client);

    void GetAudioEffects(AudioEffectNative** effects, uint* count);

    void SetAudioEffectState(Guid effectId, int state);
}

[GeneratedComInterface]
[Guid("f4ae25b5-aaa3-437d-b6b3-dbbe2d0e9549")]
internal partial interface IAcousticEchoCancellationControl
{
    void SetEchoCancellationRenderEndpoint([MarshalAs(UnmanagedType.LPWStr)] string? renderEndpointId);
}

internal static partial class CoreAudioNative
{
    public const uint ClsctxAll = 0x17;
    public const uint DeviceStateActive = 0x1;
    public const int ShareModeShared = 0;
    public const uint StreamFlagsEventCallback = 0x00040000;
    public const uint StreamFlagsSrcDefaultQuality = 0x08000000;
    public const uint StreamFlagsAutoConvertPcm = 0x80000000;
    public const uint BufferFlagsSilent = 0x2;
    public const int AudioCategoryCommunications = 3;
    public const int AudclntEDeviceInvalidated = unchecked((int)0x88890004);
    public const int ENoInterface = unchecked((int)0x80004002);

    public static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static readonly Guid IidAudioClient2 = new("726778CD-F60A-4eda-82DE-E47610CD78AA");
    public static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    public static readonly Guid IidAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
    public static readonly Guid IidAudioEffectsManager = new("4460B3AE-4B44-4527-8676-7548A8ACD260");
    public static readonly Guid IidAcousticEchoCancellationControl = new("f4ae25b5-aaa3-437d-b6b3-dbbe2d0e9549");

    /// <summary>PKEY_Device_FriendlyName (functiondiscoverykeys_devpkey.h).</summary>
    public static readonly PropertyKey DeviceFriendlyName = new()
    {
        FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        PropertyId = 14,
    };

    [LibraryImport("ole32")]
    public static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("ole32")]
    public static unsafe partial int PropVariantClear(byte* propVariant);

    private static readonly StrategyBasedComWrappers s_wrappers = new();

    /// <summary>Envolve um ponteiro COM cru (e libera a referência recebida).</summary>
    public static T Wrap<T>(nint pointer)
    {
        try
        {
            return (T)s_wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    public static IMMDeviceEnumerator CreateEnumerator()
    {
        var iid = typeof(IMMDeviceEnumerator).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ClsidMMDeviceEnumerator, 0, ClsctxAll, iid, out var pointer));
        return Wrap<IMMDeviceEnumerator>(pointer);
    }
}
