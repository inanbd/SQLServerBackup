using System.Windows;
using SqlBackup.App.Infrastructure;
using SqlBackup.App.ViewModels;
using SqlBackup.Core;

namespace SqlBackup.App;

public partial class App : Application
{
    /// <summary>True once the user chose Exit (tray menu); lets window Close mean "hide to tray" otherwise.</summary>
    public static bool ExitRequested { get; set; }

    private AppServices? _services;
    private TrayIconController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Elevated relaunches ( --install-service etc.) do their work and exit without UI.
        if (ServiceCliHandler.TryHandle(e.Args, out var exitCode))
        {
            Shutdown(exitCode);
            return;
        }

        try
        {
            AppPaths.EnsureDirectories();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(
                $"The data folder '{AppPaths.DataDir}' is not accessible: {ex.Message}\n\n" +
                "Run the installer (or create the folder and grant write access) and try again.",
                "SQL Server Backup", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        _services = new AppServices();
        var window = new MainWindow { DataContext = new MainViewModel(_services) };
        MainWindow = window;
        _tray = new TrayIconController(_services, window);
        window.Show();
    }

    public void NotifyMainWindowHidden() => _tray?.NotifyHiddenToTray();

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
