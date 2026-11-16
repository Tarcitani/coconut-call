using System.Runtime.InteropServices;
using Coconut.Audio.Interop;

namespace Coconut.Audio;

public sealed record AudioEffect(Guid Id, string Name, bool CanSetState, bool IsOn);

public sealed record AudioStreamInfo(
    string DeviceId,
    string DeviceName,
    bool CommunicationsCategory,
    double BufferMs,
    double LatencyMs,
    IReadOnlyList<AudioEffect>? Effects,
    bool? EchoReferenceControllable);

public sealed class AudioStreamOptions
{
    /// <summary>Id do dispositivo, ou nulo para o padrão de comunicação.</summary>
    public string? DeviceId { get; init; }

    /// <summary>Categoria Communications: o Windows aplica o modo de processamento de VoIP (ex.: AEC do driver).</summary>
    public bool CommunicationsCategory { get; init; } = true;

    public TimeSpan Buffer { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <summary>Captura: dispositivo de saída usado como referência do cancelamento de eco, se controlável.</summary>
    public string? EchoReferenceRenderDeviceId { get; init; }

    /// <summary>Captura: desliga a supressão de ruído por IA do Windows quando ajustável (padrão: sim, contrato do MVP).</summary>
    public bool DisableAiNoiseSuppression { get; init; } = true;
}

/// <summary>
/// Base de um fluxo WASAPI compartilhado, orientado a evento, em 48 kHz mono 16 bits (conversão pelo Windows).
/// Todo objeto COM é criado e usado dentro da thread MTA do fluxo.
/// </summary>
public abstract class AudioEndpointClient : IDisposable
{
    private readonly ManualResetEvent _stop = new(false);
    private readonly TaskCompletionSource<AudioStreamInfo> _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private int _disposed;

    /// <summary>Falha durante a execução (ex.: dispositivo removido). Chamado na thread do fluxo.</summary>
    public event Action<Exception>? Faulted;

    public bool IsDeviceLost { get; private set; }

    protected static long ToHns(TimeSpan span) => span.Ticks; // 1 tick = 100 ns

    protected Task<AudioStreamInfo> StartThread(string name)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = name, Priority = ThreadPriority.Highest };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        return _opened.Task;
    }

    /// <summary>Abre o dispositivo e devolve as informações do fluxo (na thread do fluxo).</summary>
    private protected abstract AudioStreamInfo Open(IAudioClient2 client, string deviceId, string deviceName);

    private protected abstract IMMDevice ResolveDevice();

    /// <summary>Ajustes que só valem com o fluxo em execução (na thread do fluxo).</summary>
    private protected virtual AudioStreamInfo AfterStart(IAudioClient2 client, AudioStreamInfo info) => info;

    /// <summary>Processa um evento do dispositivo (na thread do fluxo).</summary>
    private protected abstract void Service();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _stop.Set();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }

    private protected static unsafe IAudioClient2 Activate(IMMDevice device) =>
        CoreAudioNative.Wrap<IAudioClient2>(device.Activate(CoreAudioNative.IidAudioClient2, CoreAudioNative.ClsctxAll, 0));

    private protected static unsafe void InitializeShared(IAudioClient2 client, AudioStreamOptions options)
    {
        if (options.CommunicationsCategory)
        {
            var properties = new AudioClientProperties
            {
                Size = (uint)sizeof(AudioClientProperties),
                Category = CoreAudioNative.AudioCategoryCommunications,
            };
            client.SetClientProperties(&properties);
        }

        var format = new WaveFormatEx
        {
            FormatTag = 1, // PCM
            Channels = 1,
            SamplesPerSec = AudioFormat.SampleRate,
            BitsPerSample = 16,
            BlockAlign = 2,
            AvgBytesPerSec = AudioFormat.SampleRate * 2,
        };
        client.Initialize(
            CoreAudioNative.ShareModeShared,
            CoreAudioNative.StreamFlagsEventCallback | CoreAudioNative.StreamFlagsAutoConvertPcm | CoreAudioNative.StreamFlagsSrcDefaultQuality,
            ToHns(options.Buffer),
            0,
            &format,
            null);
    }

    /// <summary>Gerenciador de efeitos do fluxo, ou nulo se a API não existir (anterior ao build 22000).</summary>
    private protected static IAudioEffectsManager? EffectsManager(IAudioClient2 client) =>
        client.GetService(CoreAudioNative.IidAudioEffectsManager, out var pointer) < 0 ? null : CoreAudioNative.Wrap<IAudioEffectsManager>(pointer);

    private protected static IReadOnlyList<AudioEffect>? ReadEffects(IAudioClient2 client) =>
        EffectsManager(client) is { } manager ? QueryEffects(manager) : null;

    private protected static unsafe List<AudioEffect> QueryEffects(IAudioEffectsManager manager)
    {
        AudioEffectNative* effects = null;
        uint count = 0;
        manager.GetAudioEffects(&effects, &count);
        try
        {
            var list = new List<AudioEffect>((int)count);
            for (var i = 0; i < count; i++)
            {
                list.Add(new AudioEffect(effects[i].Id, AudioEffectNames.Of(effects[i].Id), effects[i].CanSetState != 0, effects[i].State == 1));
            }

            return list;
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)effects);
        }
    }

    private void Run()
    {
        var audioEvent = new AutoResetEvent(false);
        IAudioClient2? client = null;
        try
        {
            var device = ResolveDevice();
            var id = AudioDeviceCatalog.ReadId(device);
            client = Activate(device);
            var info = Open(client, id, id);
            client.SetEventHandle(audioEvent.SafeWaitHandle.DangerousGetHandle());
            client.Start();
            _opened.TrySetResult(AfterStart(client, info));

            WaitHandle[] handles = [audioEvent, _stop];
            while (WaitHandle.WaitAny(handles, 2000) != 1)
            {
                Service();
            }

            client.Stop();
        }
        catch (Exception ex) when (ex is COMException or AudioDeviceUnavailableException or InvalidOperationException)
        {
            IsDeviceLost = ex is COMException { HResult: CoreAudioNative.AudclntEDeviceInvalidated } or AudioDeviceUnavailableException;
            if (!_opened.TrySetException(ex))
            {
                Faulted?.Invoke(ex);
            }
        }
        finally
        {
            audioEvent.Dispose();
        }
    }
}

/// <summary>Microfone → quadros de 20 ms.</summary>
public sealed class AudioCapture : AudioEndpointClient
{
    private readonly AudioDeviceCatalog _catalog;
    private readonly AudioStreamOptions _options;
    private readonly FrameAssembler _assembler;
    private IAudioCaptureClient? _capture;
    private IAudioEffectsManager? _effects;

    /// <summary>Relê os efeitos aplicados pelo Windows (thread MTA; não usar na thread de UI).</summary>
    public IReadOnlyList<AudioEffect>? RefreshEffects() => _effects is null ? null : QueryEffects(_effects);

    /// <summary>
    /// Supressão de ruído por IA do Windows: fora do escopo do MVP e contrária ao "áudio natural".
    /// Desligada quando o sistema permite; o resultado é confirmado relendo o estado, não presumido.
    /// </summary>
    private protected override AudioStreamInfo AfterStart(IAudioClient2 client, AudioStreamInfo info)
    {
        _effects = EffectsManager(client);
        if (_effects is null || !_options.DisableAiNoiseSuppression)
        {
            return info;
        }

        if (QueryEffects(_effects).FirstOrDefault(e => e.Id == AudioEffectNames.DeepNoiseSuppression) is { CanSetState: true, IsOn: true })
        {
            _effects.SetAudioEffectState(AudioEffectNames.DeepNoiseSuppression, 0); // AUDIO_EFFECT_STATE_OFF
        }

        return info with { Effects = QueryEffects(_effects) };
    }

    private AudioCapture(AudioDeviceCatalog catalog, AudioStreamOptions options, Action<ReadOnlySpan<short>> onFrame)
    {
        _catalog = catalog;
        _options = options;
        _assembler = new FrameAssembler(AudioFormat.FrameSamples, onFrame);
    }

    /// <summary>Abre e inicia a captura. <paramref name="onFrame"/> roda na thread de captura e não deve bloquear.</summary>
    public static async Task<(AudioCapture Capture, AudioStreamInfo Info)> StartAsync(
        AudioDeviceCatalog catalog, AudioStreamOptions options, Action<ReadOnlySpan<short>> onFrame)
    {
        var capture = new AudioCapture(catalog, options, onFrame);
        try
        {
            return (capture, await capture.StartThread("Coconut captura"));
        }
        catch
        {
            capture.Dispose();
            throw;
        }
    }

    private protected override IMMDevice ResolveDevice() => _catalog.Open(_options.DeviceId, AudioDirection.Capture);

    private protected override AudioStreamInfo Open(IAudioClient2 client, string deviceId, string deviceName)
    {
        InitializeShared(client, _options);
        Marshal.ThrowExceptionForHR(client.GetService(CoreAudioNative.IidAudioCaptureClient, out var pointer));
        _capture = CoreAudioNative.Wrap<IAudioCaptureClient>(pointer);

        bool? controllable = null;
        var hr = client.GetService(CoreAudioNative.IidAcousticEchoCancellationControl, out var aec);
        if (hr >= 0)
        {
            controllable = true;
            var control = CoreAudioNative.Wrap<IAcousticEchoCancellationControl>(aec);
            if (_options.EchoReferenceRenderDeviceId is { } reference)
            {
                control.SetEchoCancellationRenderEndpoint(reference);
            }
        }
        else if (hr == CoreAudioNative.ENoInterface)
        {
            controllable = false;
        }

        return new AudioStreamInfo(
            deviceId,
            deviceName,
            _options.CommunicationsCategory,
            client.GetBufferSize() * 1000.0 / AudioFormat.SampleRate,
            client.GetStreamLatency() / 10_000.0,
            ReadEffects(client),
            controllable);
    }

    private protected override unsafe void Service()
    {
        var capture = _capture!;
        uint packet;
        while (capture.GetNextPacketSize(&packet) >= 0 && packet > 0)
        {
            byte* data;
            uint frames, flags;
            ulong devicePosition, qpcPosition;
            Marshal.ThrowExceptionForHR(capture.GetBuffer(&data, &frames, &flags, &devicePosition, &qpcPosition));
            if ((flags & CoreAudioNative.BufferFlagsSilent) != 0)
            {
                _assembler.AppendSilence((int)frames);
            }
            else
            {
                _assembler.Append(new ReadOnlySpan<short>(data, (int)frames));
            }

            capture.ReleaseBuffer(frames);
        }
    }
}

/// <summary>Quadros de 20 ms → saída de áudio.</summary>
public sealed class AudioPlayback : AudioEndpointClient
{
    private readonly AudioDeviceCatalog _catalog;
    private readonly AudioStreamOptions _options;
    private readonly FrameReader _reader;
    private IAudioClient2? _client;
    private IAudioRenderClient? _render;
    private uint _bufferFrames;

    private AudioPlayback(AudioDeviceCatalog catalog, AudioStreamOptions options, Action<Span<short>> produceFrame)
    {
        _catalog = catalog;
        _options = options;
        _reader = new FrameReader(AudioFormat.FrameSamples, produceFrame);
    }

    /// <summary>Abre e inicia a reprodução. <paramref name="produceFrame"/> roda na thread de reprodução.</summary>
    public static async Task<(AudioPlayback Playback, AudioStreamInfo Info)> StartAsync(
        AudioDeviceCatalog catalog, AudioStreamOptions options, Action<Span<short>> produceFrame)
    {
        var playback = new AudioPlayback(catalog, options, produceFrame);
        try
        {
            return (playback, await playback.StartThread("Coconut reprodução"));
        }
        catch
        {
            playback.Dispose();
            throw;
        }
    }

    private protected override IMMDevice ResolveDevice() => _catalog.Open(_options.DeviceId, AudioDirection.Render);

    private protected override unsafe AudioStreamInfo Open(IAudioClient2 client, string deviceId, string deviceName)
    {
        InitializeShared(client, _options);
        Marshal.ThrowExceptionForHR(client.GetService(CoreAudioNative.IidAudioRenderClient, out var pointer));
        _render = CoreAudioNative.Wrap<IAudioRenderClient>(pointer);
        _client = client;
        _bufferFrames = client.GetBufferSize();

        // Pré-preenche com silêncio para o primeiro período não começar vazio.
        byte* data;
        Marshal.ThrowExceptionForHR(_render.GetBuffer(_bufferFrames, &data));
        _render.ReleaseBuffer(_bufferFrames, CoreAudioNative.BufferFlagsSilent);

        return new AudioStreamInfo(
            deviceId,
            deviceName,
            _options.CommunicationsCategory,
            _bufferFrames * 1000.0 / AudioFormat.SampleRate,
            client.GetStreamLatency() / 10_000.0,
            ReadEffects(client),
            null);
    }

    private protected override unsafe void Service()
    {
        var available = _bufferFrames - _client!.GetCurrentPadding();
        if (available == 0)
        {
            return;
        }

        byte* data;
        Marshal.ThrowExceptionForHR(_render!.GetBuffer(available, &data));
        _reader.Read(new Span<short>(data, (int)available));
        _render.ReleaseBuffer(available, 0);
    }
}

internal static class AudioEffectNames
{
    // Valores de KSMedia.h (learn.microsoft.com, "Audio Signal Processing Modes").
    private static readonly Dictionary<Guid, string> Names = new()
    {
        [new Guid("6f64adbe-8211-11e2-8c70-2c27d7f001fa")] = "Cancelamento de eco (AEC)",
        [new Guid("6f64adbf-8211-11e2-8c70-2c27d7f001fa")] = "Supressão de ruído",
        [new Guid("6f64adc0-8211-11e2-8c70-2c27d7f001fa")] = "Controle automático de ganho",
        [new Guid("6f64adc1-8211-11e2-8c70-2c27d7f001fa")] = "Beamforming",
        [new Guid("6f64adc2-8211-11e2-8c70-2c27d7f001fa")] = "Remoção de tom constante",
        [new Guid("6f64adc3-8211-11e2-8c70-2c27d7f001fa")] = "Equalizador",
        [new Guid("6f64adc4-8211-11e2-8c70-2c27d7f001fa")] = "Equalizador de volume",
        [new Guid("6f64adc5-8211-11e2-8c70-2c27d7f001fa")] = "Reforço de graves",
        [new Guid("6f64adc6-8211-11e2-8c70-2c27d7f001fa")] = "Surround virtual",
        [new Guid("6f64adc7-8211-11e2-8c70-2c27d7f001fa")] = "Fones virtuais",
        [new Guid("6f64adc9-8211-11e2-8c70-2c27d7f001fa")] = "Correção de ambiente",
        [new Guid("6f64adca-8211-11e2-8c70-2c27d7f001fa")] = "Gerência de graves",
        [new Guid("6f64adcb-8211-11e2-8c70-2c27d7f001fa")] = "Efeitos ambientais",
        [new Guid("6f64adcc-8211-11e2-8c70-2c27d7f001fa")] = "Proteção de alto-falante",
        [new Guid("6f64adcd-8211-11e2-8c70-2c27d7f001fa")] = "Compensação de alto-falante",
        [new Guid("6f64adce-8211-11e2-8c70-2c27d7f001fa")] = "Compressão de faixa dinâmica",
        [new Guid("6f64add0-8211-11e2-8c70-2c27d7f001fa")] = "Supressão de ruído profunda (IA)",
    };

    public static readonly Guid DeepNoiseSuppression = new("6f64add0-8211-11e2-8c70-2c27d7f001fa");

    public static string Of(Guid id) => Names.GetValueOrDefault(id, id.ToString());
}
