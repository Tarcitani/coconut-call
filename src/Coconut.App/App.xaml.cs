using Coconut.App.Core.Services;
using Coconut.App.Core.ViewModels;
using Coconut.App.Services;
using Coconut.Audio;
using Coconut.Media.Native;
using Microsoft.UI.Xaml;

namespace Coconut.App;

/// <summary>Composição do aplicativo: serviços, ViewModel raiz e janela principal.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "WinUI owns Application lifetime; ExitAsync awaits disposal of every owned service before closing the window.")]
public partial class App : Application
{
    private MainWindow? _window;
    private ShellViewModel? _shell;
    private AudioDeviceCatalog? _devices;
    private SingleInstanceBroker? _instance;
    private TrayIcon? _tray;
    private DesktopActions? _desktop;
    private DesktopSettingsViewModel? _settings;
    private bool _exiting;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Coconut");
        if (Environment.GetEnvironmentVariable("COCONUT_PROFILE_DIRECTORY") is { Length: > 0 } configured && Path.IsPathFullyQualified(configured) && !configured.StartsWith(@"\\", StringComparison.Ordinal))
        { profile = Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar); }
        var log = new FileDiagnosticLog(Path.Combine(profile, "logs"));
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var profileKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(profile.ToUpperInvariant())))[..16];
        _instance = new SingleInstanceBroker("CoconutCall-" + identity.User!.Value + "-" + profileKey);
        var activation = AppActivation.Parse(Environment.GetCommandLineArgs().Skip(1));
        if (!_instance.IsPrimary)
        {
            try { await _instance.ForwardAsync(activation); }
            catch (Exception ex) when (ex is IOException or TimeoutException) { log.Write($"falha ao encaminhar ativação: {ex.GetType().Name}"); }
            await _instance.DisposeAsync(); Exit(); return;
        }
        if (activation.ExitIfIdle) { await _instance.DisposeAsync(); Exit(); return; }
        var version = typeof(App).Assembly.GetName().Version!;
        // Versões com 3 partes: "0.1.0.0" seria mascarado como endereço IP pelo sanitizador.
        log.Write($"Coconut v{version.ToString(3)} iniciado; Windows build {Environment.OSVersion.Version.Build}");
        UnhandledException += (_, e) => log.Write($"exceção não tratada: {e.Exception.GetType().Name} 0x{e.Exception.HResult:X8}: {e.Message}");
        TaskScheduler.UnobservedTaskException += (_, e) => log.Write($"tarefa com exceção não observada: {e.Exception.GetBaseException().GetType().Name}: {e.Exception.GetBaseException().Message}");

        var devices = _devices = new AudioDeviceCatalog();
        var window = new MainWindow();
        var shell = _shell = new ShellViewModel(
            server => new CallBackend(devices, new RtcPeerOptions { StunServers = ServerEndpoints.StunServersFor(server) }, log),
            new JsonSettingsStore(profile),
            new DispatcherQueueUiDispatcher(window.DispatcherQueue),
            new WinUiClipboard());
        window.Attach(shell);
        _window = window;
        window.ExitRequested = ExitAsync;
        _desktop = new DesktopActions(shell, window, log, ExitAsync, profile);
        _settings = new DesktopSettingsViewModel(_desktop, new DispatcherQueueUiDispatcher(window.DispatcherQueue));
        window.AttachSettings(_settings);
        try
        {
            _tray = new TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(window), Path.Combine(AppContext.BaseDirectory, "Assets", "Coconut.ico"),
                ShowWindow, () => { if (_shell.Call is { } call) { call.IsMuted = !call.IsMuted; } }, () => _ = ExitAsync(), () => _shell.Call?.IsMuted);
            window.KeepInTray = true;
        }
        catch (System.ComponentModel.Win32Exception) { log.Write("ícone da bandeja indisponível; fechar encerrará o aplicativo"); }
        _instance.Activated += request => window.DispatcherQueue.TryEnqueue(() => Activate(request));
        _instance.Listen(); _desktop.StartAutomaticChecks();
        if (!activation.Startup) { Activate(activation); }
    }

    private void ShowWindow()
    {
        if (_window is null) { return; }
        _window.AppWindow.Show(); DesktopNative.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window), 9); _window.Activate();
    }

    private void Activate(AppActivation activation)
    {
        if (activation.ExitIfIdle)
        {
            if (_shell is { Call: null } idle && !idle.Home.IsBusy) { _ = ExitAsync(); }
            else if (_shell?.Call is { } call) { call.Notice = "Encerre a chamada antes de atualizar o aplicativo."; }
            return;
        }
        if (activation.Startup) { return; }
        if (activation.Invite is { } invite) { _shell?.UseInvite(invite); }
        if (activation.InvalidInvite && _shell is { } shell) { shell.Home.Error = "O link do convite é inválido."; }
        ShowWindow();
    }

    private async Task ExitAsync()
    {
        if (_exiting) { return; } _exiting = true;
        if (_shell is not null) { await _shell.CloseAsync(); }
        if (_desktop is not null) { await _desktop.DisposeAsync(); }
        if (_instance is not null) { await _instance.DisposeAsync(); }
        _settings?.Dispose(); _tray?.Dispose(); _devices?.Dispose();
        if (_window is not null) { _window.Exiting = true; _window.Close(); }
        Exit();
    }
}
