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
    public DesktopSettingsViewModel? Settings { get; private set; }
    public bool Exiting { get; set; }
    public bool KeepInTray { get; set; }
    public Func<Task>? ExitRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Coconut.ico"));
        AppWindow.Resize(new SizeInt32(1120, 780));
        AppWindow.Closing += (sender, e) =>
        {
            if (!Exiting) { e.Cancel = true; if (KeepInTray) { AppWindow.Hide(); } else { _ = ExitRequested?.Invoke(); } }
        };
        AppWindow.Changed += (sender, e) =>
        {
            if (e.DidVisibilityChange && _shell is not null) { PageHost.Content = AppWindow.IsVisible ? _shell.CurrentPage : null; }
        };
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 680;
            presenter.PreferredMinimumHeight = 580;
        }
    }

    public void Attach(ShellViewModel shell)
    {
        _shell = shell;
        shell.PropertyChanged += OnShellChanged;
        PageHost.Content = shell.CurrentPage;
    }

    public void AttachSettings(DesktopSettingsViewModel settings) { Settings = settings; Bindings.Update(); }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentPage) && _shell is not null)
        {
            PageHost.Content = AppWindow.IsVisible ? _shell.CurrentPage : null;
        }
    }
}
