using Coconut.App.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coconut.App.Views;

/// <summary>Escolhe a view pela página atual do shell.</summary>
public sealed partial class PageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Home { get; set; }

    public DataTemplate? Call { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        HomeViewModel => Home,
        CallViewModel => Call,
        _ => null,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

/// <summary>Funções puras usadas pelo x:Bind.</summary>
public static class Bind
{
    public static bool HasText(string? text) => !string.IsNullOrEmpty(text);

    public static bool Not(bool value) => !value;

    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Nível RMS → 0..100 para barras (escala aproximada de voz).</summary>
    public static double LevelPercent(double rms) => Math.Clamp(rms / 30.0, 0, 100);

    public static string MuteGlyph(bool muted) => muted ? "" : "";

    public static string MuteLabel(bool muted) => muted ? "Ativar microfone" : "Silenciar";
}
