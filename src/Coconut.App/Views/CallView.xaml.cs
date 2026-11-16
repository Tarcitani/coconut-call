using Coconut.App.Core.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Coconut.App.Views;

/// <summary>Chamada em andamento. Composição de cabeçalho, lista e controles; sem lógica própria.</summary>
public sealed partial class CallView : UserControl
{
    public CallView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Bindings.Update();
    }

    public CallViewModel? ViewModel => DataContext as CallViewModel;
}
