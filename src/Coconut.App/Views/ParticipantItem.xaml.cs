using Coconut.App.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Coconut.App.Views;

/// <summary>
/// Cartão de um participante. Recebe o item por propriedade explícita (<see cref="Participant"/>), ligada no
/// template: dentro do ItemsRepeater, depender do DataContext deixava as ligações sem resolver.
/// </summary>
public sealed partial class ParticipantItem : UserControl
{
    public static readonly DependencyProperty ParticipantProperty = DependencyProperty.Register(
        nameof(Participant), typeof(ParticipantViewModel), typeof(ParticipantItem),
        new PropertyMetadata(null, (d, _) => ((ParticipantItem)d).Bindings.Update()));

    public ParticipantItem()
    {
        InitializeComponent();
    }

    public ParticipantViewModel? Participant
    {
        get => (ParticipantViewModel?)GetValue(ParticipantProperty);
        set => SetValue(ParticipantProperty, value);
    }

    /// <summary>Destaque de fala com a cor de ênfase do sistema; borda comum em silêncio.</summary>
    public static Brush SpeakingBrush(bool speaking) => speaking
        ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
        : (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

    /// <summary>Visível só quando explicitamente verdadeiro (nulo/ausente = oculto).</summary>
    public static Visibility VisibleIfTrue(bool? value) => value == true ? Visibility.Visible : Visibility.Collapsed;
}
