using Coconut.Audio;
using Coconut.Signaling.Contracts;


namespace Coconut.Client.Voice;

/// <summary>
/// Voz de uma chamada: microfone → <see cref="VoiceTransmitter"/> → malha, e malha → <see cref="VoiceReceiver"/> → saída.
/// Começa nos dispositivos padrão de comunicação, permite escolha manual, troca a quente e se recupera quando
/// um dispositivo some (volta ao padrão).
/// </summary>
public sealed class VoiceEngine : IAsyncDisposable
{
    private static readonly TimeSpan RecoveryDelay = TimeSpan.FromMilliseconds(500);

    private readonly MeshSession _session;
    private readonly AudioDeviceCatalog _catalog;
    private readonly SemaphoreSlim _reopen = new(1, 1);
    private readonly VoiceTransmitter _transmitter;
    private readonly VoiceReceiver _receiver = new();
    private AudioCapture? _capture;
    private AudioPlayback? _playback;
    private bool _disposed;

    public VoiceEngine(MeshSession session, AudioDeviceCatalog catalog)
    {
        _session = session;
        _catalog = catalog;
        _transmitter = new VoiceTransmitter(session.SendAudio);
        _session.AudioReceived += _receiver.Push;
        _session.MessageHandled += OnMessage;
        _catalog.DefaultChanged += OnDefaultChanged;
    }

    /// <summary>Mensagem legível para o usuário (ex.: microfone desconectado).</summary>
    public event Action<string>? Problem;

    public event Action<bool>? MuteChanged;

    /// <summary>Dispositivo escolhido; nulo = segue o padrão de comunicação do Windows.</summary>
    public string? SelectedCaptureId { get; private set; }

    public string? SelectedRenderId { get; private set; }

    public AudioStreamInfo? CaptureInfo { get; private set; }

    public AudioStreamInfo? PlaybackInfo { get; private set; }

    public TransmitStats TransmitStats => _transmitter.Stats;

    public VoiceReceiver Receiver => _receiver;

    public double MicrophoneLevel => _transmitter.LastCaptureLevel;

    public bool IsMuted
    {
        get => _transmitter.IsMuted;
        set
        {
            if (_transmitter.IsMuted != value)
            {
                _transmitter.IsMuted = value;
                MuteChanged?.Invoke(value);
            }
        }
    }

    public async Task StartAsync()
    {
        await Reopen(AudioDirection.Render);
        await Reopen(AudioDirection.Capture);
    }

    public async Task SetCaptureDeviceAsync(string? deviceId)
    {
        SelectedCaptureId = deviceId;
        await Reopen(AudioDirection.Capture);
    }

    /// <summary>Troca a saída; a captura é reaberta para o cancelamento de eco usar a nova referência.</summary>
    public async Task SetRenderDeviceAsync(string? deviceId)
    {
        SelectedRenderId = deviceId;
        await Reopen(AudioDirection.Render);
        await Reopen(AudioDirection.Capture);
    }

    public async ValueTask DisposeAsync()
    {
        await _reopen.WaitAsync();
        try
        {
            _disposed = true;
            _session.AudioReceived -= _receiver.Push;
            _session.MessageHandled -= OnMessage;
            _catalog.DefaultChanged -= OnDefaultChanged;
            _capture?.Dispose();
            _playback?.Dispose();
            _transmitter.Dispose();
            _receiver.Dispose();
        }
        finally
        {
            _reopen.Release();
        }

        _reopen.Dispose();
    }

    private async Task Reopen(AudioDirection direction)
    {
        await _reopen.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            if (direction == AudioDirection.Capture)
            {
                _capture?.Dispose();
                _capture = null;
                var options = new AudioStreamOptions
                {
                    DeviceId = SelectedCaptureId,
                    EchoReferenceRenderDeviceId = PlaybackInfo?.DeviceId ?? _catalog.DefaultDeviceId(AudioDirection.Render),
                };
                (_capture, CaptureInfo) = await OpenOrFallback(
                    options, o => AudioCapture.StartAsync(_catalog, o, _transmitter.OnCapturedFrame), id => SelectedCaptureId = id);
                _capture.Faulted += fault => _ = RecoverAsync(AudioDirection.Capture);
            }
            else
            {
                _playback?.Dispose();
                _playback = null;
                (_playback, PlaybackInfo) = await OpenOrFallback(
                    new AudioStreamOptions { DeviceId = SelectedRenderId }, o => AudioPlayback.StartAsync(_catalog, o, _receiver.Produce), id => SelectedRenderId = id);
                _playback.Faulted += fault => _ = RecoverAsync(AudioDirection.Render);
            }
        }
        catch (AudioDeviceUnavailableException ex)
        {
            Problem?.Invoke(ex.Message);
        }
        finally
        {
            _reopen.Release();
        }
    }

    /// <summary>Abre o escolhido; se ele não existir mais, volta ao padrão e avisa.</summary>
    private async Task<(T Stream, AudioStreamInfo Info)> OpenOrFallback<T>(
        AudioStreamOptions options, Func<AudioStreamOptions, Task<(T, AudioStreamInfo)>> open, Action<string?> resetSelection)
    {
        try
        {
            return await open(options);
        }
        catch (System.Runtime.InteropServices.COMException) when (options.DeviceId is not null)
        {
            resetSelection(null);
            Problem?.Invoke("O dispositivo escolhido não está disponível; usando o padrão do Windows.");
            return await open(new AudioStreamOptions { EchoReferenceRenderDeviceId = options.EchoReferenceRenderDeviceId });
        }
    }

    private async Task RecoverAsync(AudioDirection direction)
    {
        Problem?.Invoke(direction == AudioDirection.Capture
            ? "O microfone foi desconectado; tentando o padrão do Windows."
            : "A saída de áudio foi desconectada; tentando o padrão do Windows.");
        await Task.Delay(RecoveryDelay);
        if (direction == AudioDirection.Capture)
        {
            SelectedCaptureId = null;
        }
        else
        {
            SelectedRenderId = null;
        }

        await Reopen(direction);
        if (direction == AudioDirection.Render)
        {
            await Reopen(AudioDirection.Capture); // referência do cancelamento de eco mudou
        }
    }

    private void OnDefaultChanged(AudioDirection direction)
    {
        var followsDefault = direction == AudioDirection.Capture ? SelectedCaptureId is null : SelectedRenderId is null;
        if (followsDefault)
        {
            _ = direction == AudioDirection.Capture ? Reopen(AudioDirection.Capture) : SetRenderDeviceAsync(null);
        }
    }

    private void OnMessage(ServerMessage message)
    {
        switch (message)
        {
            case PeerLeft m:
                _receiver.Remove(m.Peer);
                break;
            case DropPeer m:
                _receiver.Remove(m.Peer);
                break;
        }
    }
}
