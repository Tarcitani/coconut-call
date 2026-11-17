using System.Collections.ObjectModel;
using Coconut.Client.Screen;
using Coconut.Screen;
using Coconut.Signaling.Contracts;
using Coconut.App.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coconut.App.Core.ViewModels;

public sealed partial class ScreenShareViewModel : ObservableObject, IDisposable
{
    private readonly IScreenSharing _screen;
    private readonly IUiDispatcher _ui;
    private readonly Func<ParticipantId?> _you;
    private readonly Func<ParticipantId, string> _name;

    public ScreenShareViewModel(IScreenSharing screen, IUiDispatcher ui, Func<ParticipantId?> you, Func<ParticipantId, string> name)
    {
        _screen = screen; _ui = ui; _you = you; _name = name;
        screen.Changed += OnChanged; screen.Problem += OnProblem;
        RefreshMonitors(); Refresh();
    }

    public IScreenSharing Backend => _screen;
    public ObservableCollection<ScreenMonitor> Monitors { get; } = [];
    public static IReadOnlyList<int> Resolutions { get; } = [480, 720, 1080];
    public static IReadOnlyList<int> FrameRates { get; } = [15, 30, 60];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestCommand))]
    public partial ScreenMonitor? SelectedMonitor { get; set; }
    [ObservableProperty]
    public partial int SelectedHeight { get; set; } = 720;
    [ObservableProperty]
    public partial int SelectedFps { get; set; } = 30;
    [ObservableProperty]
    public partial double BitrateKbps { get; set; } = 2000;
    [ObservableProperty]
    public partial bool ForceSoftware { get; set; }
    [ObservableProperty]
    public partial bool IsSharing { get; private set; }
    [ObservableProperty]
    public partial bool HasRemoteScreen { get; private set; }
    [ObservableProperty]
    public partial bool HasIncomingRequest { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestCommand))]
    public partial bool IsRequesting { get; private set; }
    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;
    [ObservableProperty]
    public partial string RequestText { get; private set; } = string.Empty;
    [ObservableProperty]
    public partial string? Notice { get; set; }

    public string ShareButtonText => _screen.Sharer is null ? "Compartilhar monitor" : "Pedir para compartilhar";

    public void Dispose() { _screen.Changed -= OnChanged; _screen.Problem -= OnProblem; }

    private bool CanRequest() => SelectedMonitor is not null && !IsRequesting && !IsSharing;

    [RelayCommand(CanExecute = nameof(CanRequest))]
    private async Task RequestAsync()
    {
        try { await _screen.RequestAsync(SelectedMonitor!, new ScreenOptions(SelectedHeight, SelectedFps, (int)(BitrateKbps * 1000), ForceSoftware)); }
        catch (ArgumentException) { Notice = "Escolha uma resolução, FPS e taxa válidos."; }
    }
    [RelayCommand]
    private Task StopAsync() => _screen.StopAsync();
    [RelayCommand]
    private Task AcceptAsync() => _screen.RespondAsync(true);
    [RelayCommand]
    private Task DeclineAsync() => _screen.RespondAsync(false);
    [RelayCommand]
    private Task CancelAsync() => _screen.CancelAsync();
    [RelayCommand]
    private void DismissNotice() => Notice = null;
    [RelayCommand]
    private void RefreshMonitors()
    {
        try
        {
            var selected = SelectedMonitor?.Handle;
            Monitors.Clear(); foreach (var monitor in _screen.Monitors()) { Monitors.Add(monitor); }
            SelectedMonitor = Monitors.FirstOrDefault(m => m.Handle == selected) ?? Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();
        }
        catch (System.ComponentModel.Win32Exception) { Notice = "Não foi possível listar os monitores."; }
    }
    private void OnChanged() => _ui.Post(Refresh);
    private void OnProblem(string notice) => _ui.Post(() => Notice = notice);
    private void Refresh()
    {
        IsSharing = _screen.Sharer is not null && _screen.Sharer == _you();
        HasRemoteScreen = _screen.Sharer is not null && _screen.Sharer != _you();
        HasIncomingRequest = _screen.IncomingRequest is not null;
        IsRequesting = _screen.IsRequesting;
        RequestText = _screen.IncomingRequest is { } request ? $"{_name(request.Requester)} quer compartilhar a tela. Aceitar encerra sua transmissão antes de iniciar a outra." : string.Empty;
        Status = _screen.Sharer is { } sharer && sharer != _you() ? $"Tela de {_name(sharer)}" : _screen.Status;
        OnPropertyChanged(nameof(ShareButtonText)); RequestCommand.NotifyCanExecuteChanged();
    }
}
