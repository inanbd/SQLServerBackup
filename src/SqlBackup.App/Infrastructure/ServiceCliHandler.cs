using System.Windows;

namespace SqlBackup.App.Infrastructure;

/// <summary>
/// Handles the elevated CLI verbs the app relaunches itself with:
///   --install-service [path-to-service-exe]
///   --uninstall-service | --start-service | --stop-service
/// Runs without showing the main window; errors surface as message boxes because
/// the elevated relaunch is always interactive.
/// </summary>
public static class ServiceCliHandler
{
    public static bool TryHandle(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0)
            return false;

        switch (args[0])
        {
            case "--install-service":
            {
                var serviceExe = args.Length > 1 ? args[1] : ServiceManager.FindServiceExecutable();
                if (serviceExe is null)
                {
                    ShowError("SqlBackup.Service.exe could not be found next to the application.");
                    exitCode = 2;
                    return true;
                }
                var (code, log) = ServiceManager.InstallService(serviceExe);
                exitCode = ReportResult("install", code, log);
                return true;
            }
            case "--uninstall-service":
            {
                var (code, log) = ServiceManager.UninstallService();
                exitCode = ReportResult("uninstall", code, log);
                return true;
            }
            case "--start-service":
            {
                var (code, log) = ServiceManager.StartService();
                exitCode = ReportResult("start", code, log);
                return true;
            }
            case "--stop-service":
            {
                var (code, log) = ServiceManager.StopService();
                exitCode = ReportResult("stop", code, log);
                return true;
            }
            default:
                return false;
        }
    }

    private static int ReportResult(string operation, int exitCode, string log)
    {
        if (exitCode != 0)
            ShowError($"Service {operation} failed (exit code {exitCode}).\n\n{log}");
        return exitCode;
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, "SQL Server Backup", MessageBoxButton.OK, MessageBoxImage.Error);
}
