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
        var log = new FileDiagnosticLog();
        var version = typeof(App).Assembly.GetName().Version!;
        // Versões com 3 partes: "0.1.0.0" seria mascarado como endereço IP pelo sanitizador.
        log.Write($"Coconut v{version.ToString(3)} iniciado; Windows build {Environment.OSVersion.Version.Build}");
        UnhandledException += (_, e) => log.Write($"exceção não tratada: {e.Exception.GetType().Name} 0x{e.Exception.HResult:X8}: {e.Message}");
        TaskScheduler.UnobservedTaskException += (_, e) => log.Write($"tarefa com exceção não observada: {e.Exception.GetBaseException().GetType().Name}: {e.Exception.GetBaseException().Message}");

        var devices = new AudioDeviceCatalog();
        var window = new MainWindow();
        var shell = new ShellViewModel(
            server => new CallBackend(devices, new RtcPeerOptions { StunServers = ServerEndpoints.StunServersFor(server) }, log),
            new JsonSettingsStore(),
            new DispatcherQueueUiDispatcher(window.DispatcherQueue),
            new WinUiClipboard());
        window.Attach(shell);
        window.Closed += (_, _) => devices.Dispose();
        _window = window;
        _window.Activate();
    }
}
