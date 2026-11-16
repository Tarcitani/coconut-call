using Coconut.App.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coconut.App.Views;

/// <summary>Tela inicial. Sem lógica: tudo no <see cref="HomeViewModel"/>.</summary>
public sealed partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bindings.Update();
    }

    public static string IpNotice => HomeViewModel.IpNotice;

    public HomeViewModel? ViewModel => DataContext as HomeViewModel;
}
