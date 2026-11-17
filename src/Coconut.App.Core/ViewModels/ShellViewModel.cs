using Coconut.App.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Coconut.App.Core.ViewModels;

/// <summary>Navegação entre a tela inicial e a chamada. A janela principal só hospeda <see cref="CurrentPage"/>.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IUiDispatcher _ui;
    private readonly IClipboard _clipboard;
    private Task _ending = Task.CompletedTask;

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

    /// <summary>Receiving a link fills the form; it never joins or ends an existing call.</summary>
    public void UseInvite(string invite)
    {
        Home.UseInvite(invite);
        if (Call is { } call) { call.Notice = "Convite recebido. Saia da chamada atual para revisar e entrar na outra sala."; }
    }

    private void OnJoined(ICallBackend backend) =>
        CurrentPage = new CallViewModel(backend, _ui, _clipboard, OnEnded);

    public async Task CloseAsync()
    {
        await Home.StopAsync();
        if (Call is { } call) { await call.LeaveCommand.ExecuteAsync(null); }
        await _ending;
    }

    private void OnEnded(CallViewModel call, string message)
    {
        CurrentPage = Home;
        Home.Error = string.IsNullOrEmpty(message) ? null : message;
        _ending = call.DisposeAsync().AsTask();
    }
}
