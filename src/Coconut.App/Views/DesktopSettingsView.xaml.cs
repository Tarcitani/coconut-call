using Coconut.App.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coconut.App.Views;

public sealed partial class DesktopSettingsView : UserControl
{
    public DesktopSettingsView() { InitializeComponent(); DataContextChanged += (_, _) => Bindings.Update(); }
    public DesktopSettingsViewModel? ViewModel => DataContext as DesktopSettingsViewModel;
}
