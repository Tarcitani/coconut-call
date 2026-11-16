using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Coconut.Audio.Interop;

namespace Coconut.Audio;

public enum AudioDirection
{
    Capture,
    Render,
}

public sealed record AudioDeviceInfo(string Id, string Name, AudioDirection Direction, bool IsDefault);

/// <summary>
/// Dispositivos de áudio ativos e notificações de conexão/remoção/padrão. Usa o papel Communications
/// para "padrão". Chamadas COM sempre em threads MTA (nunca na thread de UI).
/// </summary>
public sealed partial class AudioDeviceCatalog : IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly NotificationClient _notifications;

    public AudioDeviceCatalog()
    {
        _enumerator = Mta.Run(CoreAudioNative.CreateEnumerator);
        _notifications = new NotificationClient(this);
        Mta.Run(() => _enumerator.RegisterEndpointNotificationCallback(_notifications));
    }

    /// <summary>Lista de dispositivos mudou (conectado, removido, estado). Chamado em thread do sistema.</summary>
    public event Action? DevicesChanged;

    /// <summary>O dispositivo padrão de comunicação mudou.</summary>
    public event Action<AudioDirection>? DefaultChanged;

    public IReadOnlyList<AudioDeviceInfo> List(AudioDirection direction) => Mta.Run(() =>
    {
        var flow = ToFlow(direction);
        var defaultId = DefaultId(flow);
        var collection = _enumerator.EnumAudioEndpoints(flow, CoreAudioNative.DeviceStateActive);
        var count = collection.GetCount();
        var result = new List<AudioDeviceInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var device = collection.Item(i);
            var id = ReadId(device);
            result.Add(new AudioDeviceInfo(id, ReadFriendlyName(device), direction, id == defaultId));
        }

        return result;
    });

    public string? DefaultDeviceId(AudioDirection direction) => Mta.Run(() => DefaultId(ToFlow(direction)));

    public void Dispose() => Mta.Run(() => _enumerator.UnregisterEndpointNotificationCallback(_notifications));

    /// <summary>Abre o dispositivo pelo id, ou o padrão de comunicação quando nulo. Chamar em thread MTA.</summary>
    internal IMMDevice Open(string? id, AudioDirection direction)
    {
        if (id is not null)
        {
            return _enumerator.GetDevice(id);
        }

        var hr = _enumerator.GetDefaultAudioEndpoint(ToFlow(direction), ERole.Communications, out var device);
        return hr >= 0 && device is not null ? device : throw new AudioDeviceUnavailableException(direction);
    }

    internal static string ReadId(IMMDevice device)
    {
        var pointer = device.GetId();
        try
        {
            return Marshal.PtrToStringUni(pointer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static unsafe string ReadFriendlyName(IMMDevice device)
    {
        var store = device.OpenPropertyStore(0); // STGM_READ
        var variant = stackalloc byte[24];       // PROPVARIANT em x64
        new Span<byte>(variant, 24).Clear();
        store.GetValue(CoreAudioNative.DeviceFriendlyName, variant);
        try
        {
            const ushort vtLpwstr = 31;
            return *(ushort*)variant == vtLpwstr ? Marshal.PtrToStringUni(*(nint*)(variant + 8)) ?? "?" : "?";
        }
        finally
        {
            _ = CoreAudioNative.PropVariantClear(variant);
        }
    }

    private string? DefaultId(EDataFlow flow) =>
        _enumerator.GetDefaultAudioEndpoint(flow, ERole.Communications, out var device) >= 0 && device is not null ? ReadId(device) : null;

    private static EDataFlow ToFlow(AudioDirection direction) => direction == AudioDirection.Capture ? EDataFlow.Capture : EDataFlow.Render;

    /// <summary>Callbacks do sistema: só disparam eventos; nunca chamam o enumerador de volta (regra do Core Audio).</summary>
    [GeneratedComClass]
    private sealed partial class NotificationClient(AudioDeviceCatalog owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState) => Raise(() => owner.DevicesChanged?.Invoke());

        public int OnDeviceAdded(string deviceId) => Raise(() => owner.DevicesChanged?.Invoke());

        public int OnDeviceRemoved(string deviceId) => Raise(() => owner.DevicesChanged?.Invoke());

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) =>
            role != ERole.Communications ? 0 : Raise(() => owner.DefaultChanged?.Invoke(flow == EDataFlow.Capture ? AudioDirection.Capture : AudioDirection.Render));

        public int OnPropertyValueChanged(string deviceId, PropertyKey key) => 0;

        private static int Raise(Action action)
        {
            // Fora da thread do sistema, para não bloquear o serviço de áudio.
            ThreadPool.QueueUserWorkItem(_ => action());
            return 0;
        }
    }
}

public sealed class AudioDeviceUnavailableException : Exception
{
    public AudioDeviceUnavailableException()
    {
    }

    public AudioDeviceUnavailableException(string message)
        : base(message)
    {
    }

    public AudioDeviceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal AudioDeviceUnavailableException(AudioDirection direction)
        : base(direction == AudioDirection.Capture ? "Nenhum microfone disponível." : "Nenhuma saída de áudio disponível.")
    {
    }
}

/// <summary>Executa em thread MTA quando chamado de uma STA (UI): objetos COM de áudio vivem só no MTA.</summary>
internal static class Mta
{
    public static T Run<T>(Func<T> work) =>
        Thread.CurrentThread.GetApartmentState() == ApartmentState.STA ? Task.Run(work).GetAwaiter().GetResult() : work();

    public static void Run(Action work) => Run(() =>
    {
        work();
        return true;
    });
}
