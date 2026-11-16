using Coconut.App.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coconut.App.Views;

public sealed partial class DeviceSettingsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(CallViewModel), typeof(DeviceSettingsView), new PropertyMetadata(null, (d, _) => ((DeviceSettingsView)d).Bindings.Update()));

    public DeviceSettingsView()
    {
        InitializeComponent();
    }

    public CallViewModel? ViewModel
    {
        get => (CallViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
