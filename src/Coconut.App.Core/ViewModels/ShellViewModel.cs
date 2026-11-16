using Coconut.App.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Coconut.App.Core.ViewModels;

/// <summary>Navegação entre a tela inicial e a chamada. A janela principal só hospeda <see cref="CurrentPage"/>.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IUiDispatcher _ui;
    private readonly IClipboard _clipboard;

    public ShellViewModel(Func<Uri, ICallBackend> backendFactory, ISettingsStore settings, IUiDispatcher ui, IClipboard clipboard)
    {
        _ui = ui;
        _clipboard = clipboard;
        Home = new HomeViewModel(backendFactory, settings, OnJoined);
        CurrentPage = Home;
    }

    public HomeViewModel Home { get; }

    [ObservableProperty]
    public partial ObservableObject CurrentPage { get; private set; }

    public CallViewModel? Call => CurrentPage as CallViewModel;

    private void OnJoined(ICallBackend backend) =>
        CurrentPage = new CallViewModel(backend, _ui, _clipboard, OnEnded);

    private async void OnEnded(CallViewModel call, string message)
    {
        CurrentPage = Home;
        Home.Error = string.IsNullOrEmpty(message) ? null : message;
        await call.DisposeAsync();
    }
}
