using Coconut.App.Core.Services;
using Coconut.Signaling.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coconut.App.Core.ViewModels;

/// <summary>Tela inicial: nome, criar sala ou entrar por código/link.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001",
    Justification = "_pending só referencia a CancellationTokenSource criada e descartada pelo using em EnterAsync.")]
public sealed partial class HomeViewModel : ObservableObject
{
    private const int MaxNameLength = 32;

    private readonly Func<ICallBackend> _backendFactory;
    private readonly ISettingsStore _settings;
    private readonly Action<ICallBackend> _onJoined;
    private CancellationTokenSource? _pending;

    public HomeViewModel(Func<ICallBackend> backendFactory, ISettingsStore settings, Action<ICallBackend> onJoined)
    {
        _backendFactory = backendFactory;
        _settings = settings;
        _onJoined = onJoined;
        var saved = settings.Load();
        DisplayName = saved.DisplayName;
        ServerAddress = saved.ServerAddress;
        ShowIpNotice = !saved.IpNoticeAcknowledged;
    }

    public static string IpNotice => UserMessages.IpNotice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRoomCommand), nameof(JoinRoomCommand))]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JoinRoomCommand))]
    public partial string InviteInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ServerAddress { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRoomCommand), nameof(JoinRoomCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool ShowIpNotice { get; private set; }

    public bool IsNameValid => DisplayName.Trim() is { Length: > 0 and <= MaxNameLength } name && !name.Any(char.IsControl);

    public bool IsInviteValid => InviteCode.TryNormalize(InviteInput) is not null;

    /// <summary>Convite recebido por link (<c>coconut://join/…</c>) ou colado.</summary>
    public void UseInvite(string invite) => InviteInput = InviteCode.TryNormalize(invite) is { } code ? InviteCode.Format(code) : invite;

    [RelayCommand]
    private void AcknowledgeIpNotice()
    {
        ShowIpNotice = false;
        _settings.Save(_settings.Load() with { IpNoticeAcknowledged = true });
    }

    [RelayCommand]
    private void Cancel() => _pending?.Cancel();

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private Task CreateRoomAsync() => EnterAsync((backend, server, ct) => backend.CreateRoomAsync(server, DisplayName.Trim(), ct));

    [RelayCommand(CanExecute = nameof(CanJoin))]
    private Task JoinRoomAsync()
    {
        var code = InviteCode.TryNormalize(InviteInput)!;
        return EnterAsync((backend, server, ct) => backend.JoinRoomAsync(server, code, DisplayName.Trim(), ct));
    }

    private bool CanCreate() => !IsBusy && IsNameValid;

    private bool CanJoin() => CanCreate() && IsInviteValid;

    private async Task EnterAsync(Func<ICallBackend, Uri, CancellationToken, Task<JoinOutcome>> enter)
    {
        if (!Uri.TryCreate(ServerAddress.Trim(), UriKind.Absolute, out var server) || server.Scheme is not ("ws" or "wss"))
        {
            Error = "Endereço do servidor inválido (use ws:// ou wss://).";
            return;
        }

        Error = null;
        IsBusy = true;
        _settings.Save(_settings.Load() with { DisplayName = DisplayName.Trim(), ServerAddress = server.ToString() });
        using var pending = _pending = new CancellationTokenSource();
        var backend = _backendFactory();
        try
        {
            var outcome = await enter(backend, server, pending.Token);
            if (outcome == JoinOutcome.Admitted)
            {
                InviteInput = string.Empty;
                _onJoined(backend);
                return;
            }

            Error = UserMessages.For(outcome);
            await backend.DisposeAsync();
        }
        finally
        {
            _pending = null;
            IsBusy = false;
        }
    }
}
