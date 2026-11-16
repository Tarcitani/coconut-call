using Coconut.App.Core.Services;
using Coconut.App.Core.ViewModels;
using Coconut.App.Services;
using Coconut.Audio;
using Coconut.Media.Native;
using Microsoft.UI.Xaml;

namespace Coconut.App;

/// <summary>Composição do aplicativo: serviços, ViewModel raiz e janela principal.</summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var devices = new AudioDeviceCatalog();
        var window = new MainWindow();
        var shell = new ShellViewModel(
            () => new CallBackend(devices, new RtcPeerOptions()),
            new JsonSettingsStore(),
            new DispatcherQueueUiDispatcher(window.DispatcherQueue),
            new WinUiClipboard());
        window.Attach(shell);
        window.Closed += (_, _) => devices.Dispose();
        _window = window;
        _window.Activate();
    }
}
