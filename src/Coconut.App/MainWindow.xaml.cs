using System.ComponentModel;
using Coconut.App.Core.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Coconut.App;

/// <summary>Janela principal: hospeda a página atual do <see cref="ShellViewModel"/>.</summary>
public sealed partial class MainWindow : Window
{
    private ShellViewModel? _shell;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(960, 680));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 420;
            presenter.PreferredMinimumHeight = 520;
        }
    }

    public void Attach(ShellViewModel shell)
    {
        _shell = shell;
        shell.PropertyChanged += OnShellChanged;
        PageHost.Content = shell.CurrentPage;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentPage) && _shell is not null)
        {
            PageHost.Content = _shell.CurrentPage;
        }
    }
}
