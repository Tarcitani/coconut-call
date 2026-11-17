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
    private int _levelTicks;

    public CallViewModel(ICallBackend backend, IUiDispatcher ui, IClipboard clipboard, Action<CallViewModel, string> onEnded)
    {
        _backend = backend;
        _ui = ui;
        _clipboard = clipboard;
        _onEnded = onEnded;
        if (backend.ScreenSharing is { } screen) { Screen = new ScreenShareViewModel(screen, ui, () => backend.You, id => backend.Roster.FirstOrDefault(p => p.Id == id)?.DisplayName ?? "Participante"); }
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

    public ScreenShareViewModel? Screen { get; }

    [ObservableProperty]
    public partial string ConnectionSummary { get; private set; } = string.Empty;

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
        Screen?.Dispose();
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
        try { _clipboard.SetText(Signaling.Contracts.InviteCode.ToLink(InviteCode)); Notice = "Link do convite copiado."; }
        catch (System.Runtime.InteropServices.ExternalException) { Notice = "A área de transferência está ocupada. Tente copiar novamente."; }
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        try { await _backend.LeaveAsync(); }
        catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or OperationCanceledException or InvalidOperationException) { }
        finally { End("left"); }
    }

    [RelayCommand(CanExecute = nameof(IsAdmin))]
    private async Task EndForAllAsync()
    {
        try { await _backend.EndRoomForAllAsync(); }
        catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or InvalidOperationException or OperationCanceledException) { Notice = "O servidor está indisponível. Tente encerrar para todos após reconectar."; }
    }

    [RelayCommand]
    private async Task KickAsync(ParticipantViewModel participant)
    {
        if (!participant.CanBeKicked) { return; }
        try { await _backend.KickAsync(participant.Id); }
        catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException or InvalidOperationException or OperationCanceledException) { Notice = "O servidor está indisponível. Tente remover o participante após reconectar."; }
    }

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
        if (_levelTicks++ % 10 == 0)
        {
            ConnectionSummary = _backend.ConnectionSummary;
            var connections = _backend.PeerConnectionStates;
            foreach (var peer in Participants) { peer.ConnectionText = peer.IsYou ? IsMuted ? "Microfone silenciado" : "Seu áudio" : connections.GetValueOrDefault(peer.Id, string.Empty); }
        }
        MicrophoneLevel = IsMuted ? 0 : _backend.MicrophoneLevel;
        var levels = _backend.SpeakingLevels;
        foreach (var participant in Participants)
        {
            participant.Level = participant.IsYou ? MicrophoneLevel : levels.GetValueOrDefault(participant.Id);
        }
    }
}
