using System.Collections.ObjectModel;
using Coconut.App.Core.Services;
using Coconut.Audio;
using Coconut.Signaling.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coconut.App.Core.ViewModels;

/// <summary>Chamada em andamento: participantes, convite, mudo, dispositivos, sair e ações do administrador.</summary>
public sealed partial class CallViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly TimeSpan LevelInterval = TimeSpan.FromMilliseconds(100);

    private readonly ICallBackend _backend;
    private readonly IUiDispatcher _ui;
    private readonly IClipboard _clipboard;
    private readonly Action<CallViewModel, string> _onEnded;
    private readonly IDisposable _levelTimer;
    private bool _ended;
    private bool _syncingDevices;

    public CallViewModel(ICallBackend backend, IUiDispatcher ui, IClipboard clipboard, Action<CallViewModel, string> onEnded)
    {
        _backend = backend;
        _ui = ui;
        _clipboard = clipboard;
        _onEnded = onEnded;
        InviteCode = backend.InviteCode ?? string.Empty;
        IsMuted = backend.IsMuted;

        _backend.Changed += OnBackendChanged;
        _backend.Problem += OnBackendProblem;
        _backend.Ended += OnBackendEnded;
        _levelTimer = ui.StartTimer(LevelInterval, UpdateLevels);

        RefreshRoster();
        RefreshDevices();
    }

    public string InviteCode { get; }

    public string InviteDisplay => Signaling.Contracts.InviteCode.Format(InviteCode);

    public ObservableCollection<ParticipantViewModel> Participants { get; } = [];

    public ObservableCollection<AudioDeviceInfo> CaptureDevices { get; } = [];

    public ObservableCollection<AudioDeviceInfo> RenderDevices { get; } = [];

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EndForAllCommand))]
    public partial bool IsAdmin { get; private set; }

    [ObservableProperty]
    public partial string? Notice { get; set; }

    [ObservableProperty]
    public partial double MicrophoneLevel { get; private set; }

    [ObservableProperty]
    public partial AudioDeviceInfo? SelectedCaptureDevice { get; set; }

    [ObservableProperty]
    public partial AudioDeviceInfo? SelectedRenderDevice { get; set; }

    public string ParticipantCountText => Participants.Count == 1 ? "1 pessoa" : $"{Participants.Count} pessoas";

    public async ValueTask DisposeAsync()
    {
        _levelTimer.Dispose();
        _backend.Changed -= OnBackendChanged;
        _backend.Problem -= OnBackendProblem;
        _backend.Ended -= OnBackendEnded;
        await _backend.DisposeAsync();
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand]
    private void CopyInvite()
    {
        _clipboard.SetText(Signaling.Contracts.InviteCode.ToLink(InviteCode));
        Notice = "Link do convite copiado.";
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        await _backend.LeaveAsync();
        End("left");
    }

    [RelayCommand(CanExecute = nameof(IsAdmin))]
    private Task EndForAllAsync() => _backend.EndRoomForAllAsync();

    [RelayCommand]
    private Task KickAsync(ParticipantViewModel participant) =>
        participant.CanBeKicked ? _backend.KickAsync(participant.Id) : Task.CompletedTask;

    [RelayCommand]
    private void DismissNotice() => Notice = null;

    partial void OnIsMutedChanged(bool value) => _backend.IsMuted = value;

    partial void OnSelectedCaptureDeviceChanged(AudioDeviceInfo? value) => _ = SelectAsync(AudioDirection.Capture, value);

    partial void OnSelectedRenderDeviceChanged(AudioDeviceInfo? value) => _ = SelectAsync(AudioDirection.Render, value);

    /// <summary>
    /// Só escolhas do usuário chegam ao motor. A pré-seleção feita por <see cref="RefreshDevices"/> não pode
    /// trocar "seguir o padrão do Windows" por um dispositivo fixo.
    /// </summary>
    private async Task SelectAsync(AudioDirection direction, AudioDeviceInfo? device)
    {
        if (_syncingDevices || device is null || device.Id == _backend.SelectedDevice(direction))
        {
            return;
        }

        await _backend.SelectDeviceAsync(direction, device.Id);
    }

    private void OnBackendChanged() => _ui.Post(() =>
    {
        RefreshRoster();
        RefreshDevices();
    });

    private void OnBackendProblem(string message) => _ui.Post(() => Notice = message);

    private void OnBackendEnded(string reason) => _ui.Post(() => End(reason));

    private void End(string reason)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        _onEnded(this, UserMessages.ForEnd(reason));
    }

    /// <summary>Sincroniza a lista com o servidor sem recriar itens (preserva estado visual).</summary>
    private void RefreshRoster()
    {
        var you = _backend.You;
        var admin = _backend.Admin;
        IsAdmin = you is not null && you == admin;
        var roster = _backend.Roster;

        for (var i = Participants.Count - 1; i >= 0; i--)
        {
            if (roster.All(p => p.Id != Participants[i].Id))
            {
                Participants.RemoveAt(i);
            }
        }

        foreach (var peer in roster)
        {
            var item = Participants.FirstOrDefault(p => p.Id == peer.Id);
            if (item is null)
            {
                item = new ParticipantViewModel(peer.Id, peer.Id == you);
                var target = item;
                item.KickCommand = new AsyncRelayCommand(() => KickAsync(target));
                Participants.Add(item);
            }

            item.DisplayName = peer.DisplayName;
            item.State = peer.State;
            item.IsAdmin = peer.Id == admin;
            item.CanBeKicked = IsAdmin && !item.IsYou;
        }

        OnPropertyChanged(nameof(ParticipantCountText));
    }

    private void RefreshDevices()
    {
        _syncingDevices = true;
        try
        {
            Sync(CaptureDevices, _backend.Devices(AudioDirection.Capture));
            Sync(RenderDevices, _backend.Devices(AudioDirection.Render));
            SelectedCaptureDevice = Current(CaptureDevices, AudioDirection.Capture);
            SelectedRenderDevice = Current(RenderDevices, AudioDirection.Render);
        }
        finally
        {
            _syncingDevices = false;
        }
    }

    private AudioDeviceInfo? Current(IEnumerable<AudioDeviceInfo> devices, AudioDirection direction) =>
        _backend.SelectedDevice(direction) is { } id ? devices.FirstOrDefault(d => d.Id == id) : devices.FirstOrDefault(d => d.IsDefault);

    private static void Sync(ObservableCollection<AudioDeviceInfo> target, IReadOnlyList<AudioDeviceInfo> source)
    {
        if (!target.Select(d => d.Id).SequenceEqual(source.Select(d => d.Id)))
        {
            target.Clear();
            foreach (var device in source)
            {
                target.Add(device);
            }
        }
    }

    private void UpdateLevels()
    {
        MicrophoneLevel = IsMuted ? 0 : _backend.MicrophoneLevel;
        var levels = _backend.SpeakingLevels;
        foreach (var participant in Participants)
        {
            participant.Level = participant.IsYou ? MicrophoneLevel : levels.GetValueOrDefault(participant.Id);
        }
    }
}
