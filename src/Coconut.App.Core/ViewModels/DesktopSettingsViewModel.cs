using Coconut.App.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coconut.App.Core.ViewModels;

public interface IDesktopActions
{
    event Action? Changed;
    bool StartWithWindows { get; }
    string UpdateStatus { get; }
    bool CanApplyUpdate { get; }
    void SetStartup(bool enabled);
    Task CheckUpdatesAsync();
    Task ApplyUpdateAsync();
    Task ExportLogsAsync();
    Task ExitAsync();
}

public sealed partial class DesktopSettingsViewModel : ObservableObject, IDisposable
{
    private readonly IDesktopActions _actions;
    private readonly IUiDispatcher _ui;
    private bool _loading;

    public DesktopSettingsViewModel(IDesktopActions actions, IUiDispatcher ui)
    {
        _actions = actions; _ui = ui; _loading = true; StartWithWindows = actions.StartWithWindows; _loading = false;
        actions.Changed += OnChanged; Refresh();
    }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }
    [ObservableProperty]
    public partial string UpdateStatus { get; private set; } = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyUpdateCommand))]
    public partial bool CanApplyUpdate { get; private set; }
    [ObservableProperty]
    public partial string? Notice { get; set; }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading) { return; }
        try { _actions.SetStartup(value); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        { _loading = true; StartWithWindows = _actions.StartWithWindows; _loading = false; Notice = "Não foi possível alterar a inicialização do Windows."; }
    }
    [RelayCommand]
    private Task CheckUpdatesAsync() => _actions.CheckUpdatesAsync();
    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    private Task ApplyUpdateAsync() => _actions.ApplyUpdateAsync();
    [RelayCommand]
    private Task ExportLogsAsync() => _actions.ExportLogsAsync();
    [RelayCommand]
    private Task ExitAsync() => _actions.ExitAsync();
    public void Dispose() => _actions.Changed -= OnChanged;
    private void OnChanged() => _ui.Post(Refresh);
    private void Refresh() { UpdateStatus = _actions.UpdateStatus; CanApplyUpdate = _actions.CanApplyUpdate; }
}
