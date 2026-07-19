using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SqlBackup.App.Infrastructure;

/// <summary>
/// Install/uninstall/start/stop commands for the Windows service, shared by the
/// dashboard and the settings page. All operations elevate via UAC.
/// </summary>
public sealed class ServiceCommands
{
    private readonly AppServices _services;
    private readonly Func<Task> _refreshAfter;

    public ServiceCommands(AppServices services, Func<Task> refreshAfter)
    {
        _services = services;
        _refreshAfter = refreshAfter;
        InstallCommand = new AsyncRelayCommand(_ => InstallAsync());
        UninstallCommand = new AsyncRelayCommand(_ => UninstallAsync());
        StartCommand = new AsyncRelayCommand(_ => RunVerbAsync("--start-service"));
        StopCommand = new AsyncRelayCommand(_ => RunVerbAsync("--stop-service"));
    }

    public ICommand InstallCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    private async Task InstallAsync()
    {
        var serviceExe = ServiceManager.FindServiceExecutable();
        if (serviceExe is null)
        {
            MessageBox.Show(
                "SqlBackup.Service.exe was not found. Install the full package, or build the Service project first.",
                "SQL Server Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await RunVerbAsync($"--install-service \"{serviceExe}\"");
    }

    private async Task UninstallAsync()
    {
        var confirmed = MessageBox.Show(
            "Remove the backup service from Windows? Scheduled backups will stop running. " +
            "Configuration and history are kept.",
            "SQL Server Backup", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmed != MessageBoxResult.Yes)
            return;
        await RunVerbAsync("--uninstall-service");
    }

    private async Task RunVerbAsync(string verb)
    {
        var (started, _) = await Task.Run(() => _services.ServiceManager.RunElevatedVerb(verb));
        if (!started)
        {
            MessageBox.Show("The operation needs administrator approval and was cancelled.",
                "SQL Server Backup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        // Give the SCM a moment to settle before refreshing the displayed state.
        await Task.Delay(500);
        await _refreshAfter();
    }
}
