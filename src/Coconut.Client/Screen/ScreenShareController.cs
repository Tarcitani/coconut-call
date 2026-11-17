using System.Diagnostics;
using System.Threading.Channels;
using Coconut.Screen;
using Coconut.Signaling.Contracts;

namespace Coconut.Client.Screen;

public sealed record AuthorizedScreenFrame(ParticipantId Owner, ShareSessionId Session, ScreenFrame Frame);

public interface IScreenSharing
{
    event Action? Changed;
    event Action<string>? Problem;
    event Action<AuthorizedScreenFrame>? FrameReceived;
    ParticipantId? Sharer { get; }
    ShareSessionId? Session { get; }
    ShareRequested? IncomingRequest { get; }
    ShareRequestPending? PendingRequest { get; }
    bool IsRequesting { get; }
    bool IsCapturing { get; }
    string Status { get; }
    IReadOnlyList<ScreenMonitor> Monitors();
    Task RequestAsync(ScreenMonitor monitor, ScreenOptions options);
    Task StopAsync();
    Task RespondAsync(bool accept);
    Task CancelAsync();
    void ReportDecoded(long frames) { }
}

/// <summary>Serializes capture lifetime and server grants. No local action can grant transmission.</summary>
public sealed class ScreenShareController : IScreenSharing, IAsyncDisposable
{
    private readonly MeshSession _session;
    private readonly Channel<Func<Task>> _operations = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker, _feedback;
    private ScreenCapture? _capture;
    private ShareSessionId? _captureSession;
    private int _captureGeneration;
    private ScreenMonitor? _selected;
    private ScreenOptions _options = new();
    private ScreenRateController? _rate;
    private bool _cancelWhenKnown, _disposed;
    private long _encoded, _deliveredBytes, _received, _decoded;

    public ScreenShareController(MeshSession session)
    {
        _session = session;
        _session.MessageHandled += OnMessage;
        _session.ScreenReceived += OnFrame;
        _session.ScreenLossReport += OnLoss;
        _session.ScreenRoundTrip += OnRoundTrip;
        _session.Ended += OnEnded;
        _session.SignalingConnectivityChanged += OnConnectivity;
        _worker = Task.Run(RunAsync);
        _feedback = Task.Run(FeedbackAsync);
    }

    public event Action? Changed;
    public event Action<string>? Problem;
    public event Action<AuthorizedScreenFrame>? FrameReceived;
    public ParticipantId? Sharer => _session.IsSignalingConnected ? _session.Sharer : null;
    public ShareSessionId? Session => _session.IsSignalingConnected ? _session.ShareSession : null;
    public ShareRequested? IncomingRequest { get; private set; }
    public ShareRequestPending? PendingRequest { get; private set; }
    public bool IsRequesting { get; private set; }
    public bool IsCapturing => _capture is not null;
    public string Status { get; private set; } = "Ninguém está compartilhando.";
    public long EncodedFrames => Interlocked.Read(ref _encoded);
    public long DeliveredBytes => Interlocked.Read(ref _deliveredBytes);
    public long ReceivedFrames => Interlocked.Read(ref _received);
    public long DecodedFrames => Interlocked.Read(ref _decoded);
    public void ReportDecoded(long frames) { if (frames > 0) { Interlocked.Add(ref _decoded, frames); } }

    public IReadOnlyList<ScreenMonitor> Monitors() => MonitorCatalog.List();

    public Task RequestAsync(ScreenMonitor monitor, ScreenOptions options) => Post(async () =>
    {
        options.Validate();
        if (IsRequesting || IsCapturing) { return; }
        if (!Monitors().Any(m => m.Handle == monitor.Handle)) { Problem?.Invoke("O monitor escolhido não está mais disponível."); return; }
        _selected = monitor; _options = options; _cancelWhenKnown = false;
        IsRequesting = true; Status = "Solicitando compartilhamento…"; Changed?.Invoke();
        await _session.RequestShareAsync();
    });

    public Task StopAsync() => Post(async () =>
    {
        _cancelWhenKnown = true;
        var session = Session;
        var awaitingAcknowledgment = IsRequesting && PendingRequest is null && Sharer != _session.You;
        await StopCaptureAsync();
        if (Sharer == _session.You && session is { } id) { await _session.ShareStoppedAsync(id); }
        else if (PendingRequest is { } pending) { await _session.CancelShareAsync(pending.Request); }
        IsRequesting = awaitingAcknowledgment; PendingRequest = null; Changed?.Invoke();
    });

    public Task RespondAsync(bool accept) => Post(async () =>
    {
        if (IncomingRequest is { } request) { await _session.RespondShareAsync(request.Request, accept); IncomingRequest = null; Changed?.Invoke(); }
    });

    public Task CancelAsync() => StopAsync();

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _session.MessageHandled -= OnMessage; _session.ScreenReceived -= OnFrame;
        _session.ScreenLossReport -= OnLoss; _session.Ended -= OnEnded;
        _session.ScreenRoundTrip -= OnRoundTrip;
        _session.SignalingConnectivityChanged -= OnConnectivity;
        await _stop.CancelAsync(); await _feedback;
        _operations.Writer.TryComplete(); await _worker;
        await StopCaptureAsync(); _stop.Dispose();
    }

    private Task Post(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_operations.Writer.TryWrite(async () =>
        {
            try { await action(); done.TrySetResult(); }
            catch (Exception ex) { IsRequesting = false; PendingRequest = null; Changed?.Invoke(); Problem?.Invoke($"Não foi possível concluir a ação de tela (0x{ex.HResult:X8})."); done.TrySetResult(); }
        })) { done.TrySetResult(); }
        return done.Task;
    }

    private async Task RunAsync()
    {
        await foreach (var action in _operations.Reader.ReadAllAsync()) { await action(); }
    }

    private void OnLoss(ParticipantId peer, byte loss) => _ = Post(() => { _rate?.Report(peer.Value, loss); return Task.CompletedTask; });
    private void OnRoundTrip(ParticipantId peer, TimeSpan roundTrip) => _ = Post(() => { _rate?.ReportRoundTrip(peer.Value, roundTrip); return Task.CompletedTask; });

    private void OnEnded(string reason) => _ = Post(async () => { _cancelWhenKnown = true; await StopCaptureAsync(); });

    private void OnConnectivity(bool connected) => _ = Post(async () =>
    {
        if (connected) { await ReconcileAsync(); }
        else { await StopCaptureAsync(); Status = "Reconectando a sinalização; a tela fica pausada até confirmar a sessão."; }
        Changed?.Invoke();
    });

    private void OnFrame(AuthorizedScreenFrame frame)
    {
        if (!_disposed && Sharer == frame.Owner && Session == frame.Session) { Interlocked.Increment(ref _received); FrameReceived?.Invoke(frame); }
    }

    private void OnMessage(ServerMessage message) => _ = Post(async () =>
    {
        switch (message)
        {
            case ShareStateChanged:
            case RoomSnapshot:
                IncomingRequest = null;
                await ReconcileAsync();
                break;
            case PeerAdmitted:
                _capture?.RequestKeyframe();
                break;
            case PeerLeft left:
                _rate?.Remove(left.Peer.Value);
                break;
            case ShareRequested request when Sharer == _session.You:
                IncomingRequest = request;
                break;
            case ShareRequestPending pending:
                PendingRequest = pending; Status = "Aguardando a resposta de quem está compartilhando…";
                if (_cancelWhenKnown) { await _session.CancelShareAsync(pending.Request); PendingRequest = null; IsRequesting = false; }
                break;
            case ShareRequestDeclined declined when PendingRequest?.Request == declined.Request:
                ClearRequest("Seu pedido de compartilhamento foi recusado.");
                break;
            case ShareRequestExpired expired when PendingRequest?.Request == expired.Request || IncomingRequest?.Request == expired.Request:
                ClearRequest("O pedido de compartilhamento expirou.");
                break;
            case ShareRequestCanceled canceled when IncomingRequest?.Request == canceled.Request:
                IncomingRequest = null;
                break;
            case ShareBusy:
                ClearRequest("Já existe um pedido de troca em andamento. Tente novamente depois.");
                break;
            case StopSharing stop:
                if (_captureSession == stop.Session) { await StopCaptureAsync(); }
                await _session.ShareStoppedAsync(stop.Session);
                break;
            case CommandRejected { Command: "requestShare" or "respondShare" or "cancelShare" }:
                ClearRequest("O servidor recusou a ação de compartilhamento.");
                break;
        }
        Changed?.Invoke();
    });

    private void ClearRequest(string notice)
    {
        IsRequesting = false; PendingRequest = null; IncomingRequest = null; Problem?.Invoke(notice);
    }

    private async Task ReconcileAsync()
    {
        if (Sharer != _session.You || Session is null)
        {
            await StopCaptureAsync();
            if (_cancelWhenKnown) { IsRequesting = false; PendingRequest = null; }
            Status = Sharer is null ? "Ninguém está compartilhando." : "Recebendo a tela compartilhada.";
            return;
        }
        if (_capture is not null) { return; }
        if (_selected is null || _cancelWhenKnown || _disposed)
        {
            await _session.ShareStoppedAsync(Session.Value); return;
        }
        IsRequesting = false; PendingRequest = null; _rate = new ScreenRateController(_options.Bitrate);
        await StartCaptureAsync(_options);
    }

    private async Task StartCaptureAsync(ScreenOptions options)
    {
        if (_selected is null || Session is not { } session || Sharer != _session.You || _disposed) { return; }
        var dimensions = options.Dimensions(_selected);
        try
        {
            _captureSession = session;
            var generation = Interlocked.Increment(ref _captureGeneration);
            var parameters = new H264ParameterSets();
            _capture = await Task.Run(() => ScreenCapture.Start(_selected, options, (data, time, key, header) =>
            {
                if (_disposed || Volatile.Read(ref _captureGeneration) != generation || Session != session || Sharer != _session.You) { return; }
                var timestamp = unchecked((uint)(Stopwatch.GetTimestamp() * (90000.0 / Stopwatch.Frequency)));
                var normalized = parameters.Normalize(data, header);
                var frame = new ScreenFrame(normalized.AnnexB, dimensions.Width, dimensions.Height, timestamp, normalized.IsKeyframe);
                Interlocked.Increment(ref _encoded);
                Interlocked.Add(ref _deliveredBytes, _session.SendScreen(session, frame));
                if (_session.You is { } you) { FrameReceived?.Invoke(new AuthorizedScreenFrame(you, session, frame)); }
            }, code => _ = Post(async () =>
            {
                if (Volatile.Read(ref _captureGeneration) != generation) { return; }
                await StopCaptureAsync(); Problem?.Invoke($"A captura foi interrompida (0x{code:X8}). Escolha o monitor novamente.");
                if (Session == session) { await _session.ShareStoppedAsync(session); }
            })));
            // A grant can be revoked while the asynchronous native constructor is opening the monitor.
            if (Session != session || Sharer != _session.You || _disposed) { await StopCaptureAsync(); return; }
            Status = $"Compartilhando {dimensions.Width} × {dimensions.Height} · até {options.FramesPerSecond} fps · {options.Bitrate / 1000} kbps por participante · {(_capture.IsHardware ? "hardware" : "software")}";
            _activeOptions = options;
        }
        catch (Exception ex)
        {
            Problem?.Invoke($"Não foi possível capturar o monitor (0x{ex.HResult:X8}). Verifique se ele continua conectado.");
            if (Session == session) { await _session.ShareStoppedAsync(session); }
        }
        Changed?.Invoke();
    }

    private async Task StopCaptureAsync()
    {
        Interlocked.Increment(ref _captureGeneration);
        var capture = _capture; _capture = null; _captureSession = null;
        if (capture is not null) { await Task.Run(capture.Dispose); }
    }

    private async Task FeedbackAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                _session.SendScreenReceiverReport();
                await Post(async () =>
                {
                    if (_capture is null || _rate is null || _selected is null) { return; }
                    var rate = _rate.Tick();
                    if (_rate.ShouldStopForCongestion)
                    {
                        var sharing = Session; await StopCaptureAsync();
                        Problem?.Invoke("A tela foi encerrada após perda intensa de pacotes, para liberar a conexão para a voz. Tente novamente com uma taxa menor.");
                        if (sharing is { } granted && _session.IsSignalingConnected) { await _session.ShareStoppedAsync(granted); }
                        Changed?.Invoke(); return;
                    }
                    var height = Math.Min(_options.MaxHeight, rate < 500_000 ? 480 : rate < 1_200_000 ? 720 : 1080);
                    var fps = Math.Min(_options.FramesPerSecond, rate < 700_000 ? 15 : rate < 1_500_000 ? 30 : 60);
                    var dimensions = (_options with { MaxHeight = height }).Dimensions(_selected);
                    if (_capture.NeedsReconfigure || _capture.Width != dimensions.Width || _capture.Height != dimensions.Height || !_activeOptions.Equals(_options with { MaxHeight = height, FramesPerSecond = fps, Bitrate = rate }))
                    {
                        // Reopen only for format/fps changes; bitrate alone is changed in-place.
                        if (_capture.NeedsReconfigure || _capture.Width != dimensions.Width || _capture.Height != dimensions.Height || _activeOptions.FramesPerSecond != fps)
                        {
                            await StopCaptureAsync(); await StartCaptureAsync(_options with { MaxHeight = height, FramesPerSecond = fps, Bitrate = rate });
                        }
                        else { _capture.SetBitrate(rate); Status = $"Compartilhando {_capture.Width} × {_capture.Height} · até {fps} fps · {rate / 1000} kbps por participante · {(_capture.IsHardware ? "hardware" : "software")}"; Changed?.Invoke(); }
                        _activeOptions = _options with { MaxHeight = height, FramesPerSecond = fps, Bitrate = rate };
                    }
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private ScreenOptions _activeOptions = new();
}
