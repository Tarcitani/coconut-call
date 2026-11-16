using Coconut.Signaling.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Coconut.App.Core.ViewModels;

/// <summary>Um participante na lista da chamada.</summary>
public sealed partial class ParticipantViewModel(ParticipantId id, bool isYou) : ObservableObject
{
    /// <summary>Nível RMS acima do qual o participante é mostrado como falando.</summary>
    public const double SpeakingThreshold = 300;

    public ParticipantId Id { get; } = id;

    public bool IsYou { get; } = isYou;

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial ParticipantState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsAdmin { get; set; }

    [ObservableProperty]
    public partial bool CanBeKicked { get; set; }

    /// <summary>Expulsar este participante (atribuído pela chamada; só efetivo para o administrador).</summary>
    public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand? KickCommand { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpeaking))]
    public partial double Level { get; set; }

    public bool IsSpeaking => Level >= SpeakingThreshold;

    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "?";

    public string StatusText => (State, IsAdmin, IsYou) switch
    {
        (ParticipantState.Reconnecting, _, _) => "Reconectando…",
        (ParticipantState.Pending, _, _) => "Entrando…",
        (_, true, true) => "Você · administrador",
        (_, true, false) => "Administrador",
        (_, false, true) => "Você",
        _ => string.Empty,
    };

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(Initial));
}
