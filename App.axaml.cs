using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Murmel.Services;

namespace Murmel;

public partial class App : Application
{
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Only quit when the tray "Beenden" explicitly shuts us down - hiding the
            // window (minimize to tray) must never end the process, since the global
            // hotkey needs to keep working in the background.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;

            // Launched via the "start with Windows" registry entry: come up quietly in
            // the background instead of popping the window open at every login.
            bool startMinimized = desktop.Args?.Contains(AutostartService.MinimizedStartupArg) == true;
            if (startMinimized)
            {
                _mainWindow.Opened += (_, _) => _mainWindow.StartHiddenInBackground();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayShowClicked(object? sender, System.EventArgs e)
    {
        _mainWindow?.RestoreMainWindow();
    }

    private void OnTrayExitClicked(object? sender, System.EventArgs e)
    {
        if (_mainWindow is not null)
            _mainWindow.AllowRealClose = true;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
