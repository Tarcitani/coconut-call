using System.ComponentModel;
using System.Threading.Channels;
using Coconut.App.Core.ViewModels;
using Coconut.Client.Screen;
using Coconut.Screen;
using Coconut.Signaling.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coconut.App.Views;

/// <summary>Bounded latest-frame queue; decode work never runs on the UI or audio thread.</summary>
public sealed partial class ScreenShareView : UserControl, INotifyPropertyChanged
{
    private ScreenShareViewModel? _attached;
    private ScreenRenderer? _renderer;
    private Channel<AuthorizedScreenFrame>? _frames;
    private ShareSessionId? _renderSession;
    private int _width, _height, _initializing;
    private bool _loaded;
    private ShareSessionId? _failedSession;

    public ScreenShareView()
    {
        InitializeComponent();
        Loaded += (_, _) => { _loaded = true; Attach(); };
        Unloaded += (_, _) => { _loaded = false; Detach(); };
        DataContextChanged += (_, _) => { if (_loaded) { Detach(); Attach(); } Bindings.Update(); };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ScreenShareViewModel? ViewModel => DataContext as ScreenShareViewModel;
    public bool HasScreen => ViewModel?.Backend.Sharer is not null;
    public bool CanChoose => ViewModel is { IsSharing: false, IsRequesting: false };

    private void Attach()
    {
        if (ViewModel is not { } vm || _attached is not null) { return; }
        _attached = vm; vm.PropertyChanged += OnChanged;
        vm.Backend.FrameReceived += OnFrame;
        _frames = Channel.CreateBounded<AuthorizedScreenFrame>(new BoundedChannelOptions(2) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
        _ = DecodeAsync(_frames.Reader);
    }

    private void Detach()
    {
        if (_attached is { } vm) { vm.PropertyChanged -= OnChanged; vm.Backend.FrameReceived -= OnFrame; }
        _attached = null; _frames?.Writer.TryComplete(); _frames = null;
        ResetRenderer();
    }

    private void OnSettingsOpening(object? sender, object e) => ViewModel?.RefreshMonitorsCommand.Execute(null);

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasScreen)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanChoose)));
        if (_renderSession != _attached?.Backend.Session) { ResetRenderer(); }
    }

    private void OnFrame(AuthorizedScreenFrame frame)
    {
        if (!Authorized(frame)) { return; }
        if (_failedSession == frame.Session) { return; }
        if (_renderer is not null && _renderSession == frame.Session && _width == frame.Frame.Width && _height == frame.Frame.Height)
        {
            _frames?.Writer.TryWrite(frame); return;
        }
        if (!frame.Frame.IsKeyframe) { return; }
        if (Interlocked.Exchange(ref _initializing, 1) != 0) { return; }
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!Authorized(frame)) { return; }
                _renderer?.Dispose(); _renderer = null;
                _width = frame.Frame.Width; _height = frame.Frame.Height;
                VideoPanel.Width = _width; VideoPanel.Height = _height;
                var renderer = new ScreenRenderer(_width, _height);
                try { renderer.Attach(((WinRT.IWinRTObject)VideoPanel).NativeObject.ThisPtr); }
                catch { renderer.Dispose(); throw; }
                _renderer = renderer; _renderSession = frame.Session; _frames?.Writer.TryWrite(frame);
            }
            catch (Exception ex) { _failedSession = frame.Session; if (_attached is { } vm) { vm.Notice = $"Não foi possível exibir a tela (0x{ex.HResult:X8})."; } }
            finally { Interlocked.Exchange(ref _initializing, 0); }
        });
    }

    private bool Authorized(AuthorizedScreenFrame frame) => _attached is { } vm && vm.Backend.Session == frame.Session && vm.Backend.Sharer == frame.Owner;

    private void ResetRenderer()
    {
        if (_renderer is not null) { ScreenRenderer.Clear(((WinRT.IWinRTObject)VideoPanel).NativeObject.ThisPtr); }
        _renderer?.Dispose(); _renderer = null; _renderSession = null; _failedSession = null;
    }

    private async Task DecodeAsync(ChannelReader<AuthorizedScreenFrame> frames)
    {
        await Task.Run(async () =>
        {
            await foreach (var frame in frames.ReadAllAsync())
            {
                if (!Authorized(frame) || _renderSession != frame.Session || _width != frame.Frame.Width || _height != frame.Frame.Height) { continue; }
                try
                {
                    if (_renderer is { } renderer)
                    {
                        var before = renderer.Frames; renderer.Push(frame.Frame.AnnexB, (long)frame.Frame.Timestamp * 1000 / 9);
                        var after = renderer.Frames; if (after > before) { _attached?.Backend.ReportDecoded((long)(after - before)); }
                    }
                }
                catch (Exception ex) { DispatcherQueue.TryEnqueue(() => { if (_attached is { } vm) { vm.Notice = $"Falha ao decodificar a tela (0x{ex.HResult:X8})."; } }); }
            }
        });
    }
}
