using System.Diagnostics;
using System.Security.Cryptography;
using Coconut.App.Core.Services;
using Coconut.App.Core.ViewModels;
using Coconut.Updates;
using Microsoft.Win32;
using Windows.Storage.Pickers;

namespace Coconut.App.Services;

internal sealed class DesktopActions : IDesktopActions, IAsyncDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly ShellViewModel _shell;
    private readonly MainWindow _window;
    private readonly FileDiagnosticLog _log;
    private readonly Func<Task> _exit;
    private readonly VerifiedUpdateClient _updates;
    private readonly SemaphoreSlim _check = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _highestPath;
    private Version _highest;
    private Task? _automatic;
    private VerifiedUpdate? _pending;
    private volatile bool _closing;

    public DesktopActions(ShellViewModel shell, MainWindow window, FileDiagnosticLog log, Func<Task> exit, string profile)
    {
        _shell = shell; _window = window; _log = log; _exit = exit;
        var installed = typeof(App).Assembly.GetName().Version!;
        _highest = new Version(installed.Major, installed.Minor, installed.Build);
        var directory = Path.Combine(profile, "updates");
        _highestPath = Path.Combine(directory, "highest-version.txt");
        if (File.Exists(_highestPath) && Version.TryParse(File.ReadAllText(_highestPath).Trim(), out var saved) && saved > _highest) { _highest = saved; }
        _updates = new VerifiedUpdateClient(ManifestVerifier.EmbeddedPublicKey(), new Version(installed.Major, installed.Minor, installed.Build), directory);
    }

    public event Action? Changed;
    public bool StartWithWindows => Registry.GetValue(@"HKEY_CURRENT_USER\" + RunKey, "CoconutCall", null) is string;
    public string UpdateStatus { get; private set; } = "Atualizações são verificadas ao abrir e a cada 6 horas. A instalação aguarda o fim da chamada.";
    public bool CanApplyUpdate => _pending is not null;

    public void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) { key.SetValue("CoconutCall", $"\"{Environment.ProcessPath}\" --startup", RegistryValueKind.String); }
        else { key.DeleteValue("CoconutCall", throwOnMissingValue: false); }
    }

    public void StartAutomaticChecks() => _automatic ??= Task.Run(async () =>
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), _stop.Token);
            await CheckUpdatesAsync();
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            while (await timer.WaitForNextTickAsync(_stop.Token)) { await CheckUpdatesAsync(); }
        }
        catch (OperationCanceledException) { }
    });

    public async Task CheckUpdatesAsync()
    {
        if (_closing) { return; }
        if (!await _check.WaitAsync(0, _stop.Token)) { return; }
        try
        {
            UpdateStatus = "Verificando assinatura das atualizações…"; Changed?.Invoke();
            var manifest = await _updates.CheckAsync(_stop.Token);
            if (manifest is null) { UpdateStatus = _pending is null ? "Você está usando a versão mais recente disponível para este canal." : $"Versão {_pending.Manifest.Version} verificada e pronta para instalar."; return; }
            if (Version.Parse(manifest.Version) < _highest) { throw new CryptographicException("Versão anterior à maior atualização já verificada."); }
            if (_pending?.Manifest.Version == manifest.Version) { return; }
            UpdateStatus = $"Baixando a versão {manifest.Version} com verificação de integridade…"; Changed?.Invoke();
            _pending = await _updates.DownloadAsync(manifest, _stop.Token); _highest = Version.Parse(manifest.Version);
            Directory.CreateDirectory(Path.GetDirectoryName(_highestPath)!); File.WriteAllText(_highestPath, _highest.ToString(3));
            UpdateStatus = $"Versão {manifest.Version} verificada e pronta. Encerre a chamada antes de instalar.";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or CryptographicException or FormatException or System.Text.Json.JsonException or OperationCanceledException or InvalidOperationException or KeyNotFoundException)
        {
            UpdateStatus = "Não foi possível verificar uma atualização segura. Seu aplicativo continua funcionando.";
            _log.Write($"atualização recusada ou indisponível: {ex.GetType().Name}");
        }
        finally { _check.Release(); Changed?.Invoke(); }
    }

    public async Task ApplyUpdateAsync()
    {
        if (_pending is null) { return; }
        try
        {
            var applied = await VerifiedUpdateClient.ApplyAsync(_pending, () => _shell.Call is not null || _shell.Home.IsBusy,
                path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, Arguments = "/SP- /SILENT /NORESTART /COCONUTUPDATE=1" }), _stop.Token);
            if (applied) { await _exit(); }
            else { UpdateStatus = "Saia da chamada antes de instalar a atualização. Ela não será interrompida."; }
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or System.ComponentModel.Win32Exception)
        { UpdateStatus = "A atualização foi bloqueada porque o pacote não pôde ser validado ou iniciado."; _log.Write($"aplicação de atualização recusada: {ex.GetType().Name}"); }
        Changed?.Invoke();
    }

    public async Task ExportLogsAsync()
    {
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = $"Coconut-diagnostico-{DateTime.Now:yyyyMMdd}" };
            picker.FileTypeChoices.Add("Arquivo ZIP", new List<string> { ".zip" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
            var file = await picker.PickSaveFileAsync();
            if (file is not null) { await DiagnosticExporter.ExportAsync(_log.Directory, file.Path); UpdateStatus = "Diagnóstico exportado. Você decide com quem compartilhar o ZIP."; Changed?.Invoke(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { UpdateStatus = "Não foi possível exportar o diagnóstico."; Changed?.Invoke(); }
    }

    public Task ExitAsync() => _exit();

    public async ValueTask DisposeAsync()
    {
        _closing = true;
        await _stop.CancelAsync(); if (_automatic is not null) { await _automatic; }
        await _check.WaitAsync(); _check.Release();
        _updates.Dispose(); _stop.Dispose(); _check.Dispose();
    }
}
