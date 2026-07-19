using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text;
using SqlBackup.Core;

namespace SqlBackup.App.Infrastructure;

public enum ServiceInstallState
{
    NotInstalled,
    Running,
    Stopped,
    Transitioning,
}

/// <summary>
/// Installs/uninstalls/starts/stops the Windows service. Status queries work from
/// any account; state changes relaunch this executable elevated with a CLI verb
/// (see <see cref="ServiceCliHandler"/>), which then drives sc.exe.
/// </summary>
public sealed class ServiceManager
{
    public ServiceInstallState GetState()
    {
        try
        {
            using var controller = new ServiceController(ServiceConstants.ServiceName);
            return controller.Status switch
            {
                ServiceControllerStatus.Running => ServiceInstallState.Running,
                ServiceControllerStatus.Stopped => ServiceInstallState.Stopped,
                _ => ServiceInstallState.Transitioning,
            };
        }
        catch (InvalidOperationException)
        {
            return ServiceInstallState.NotInstalled;
        }
    }

    /// <summary>Locates SqlBackup.Service.exe next to the app (installer layout) or in the dev tree.</summary>
    public static string? FindServiceExecutable()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "SqlBackup.Service.exe"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "Service", "SqlBackup.Service.exe")),
            // Development tree: src/SqlBackup.App/bin/<cfg>/net8.0-windows -> src/SqlBackup.Service/bin/<cfg>/net8.0
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "SqlBackup.Service", "bin", "Debug", "net8.0", "SqlBackup.Service.exe")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "SqlBackup.Service", "bin", "Release", "net8.0", "SqlBackup.Service.exe")),
        };
        return Array.Find(candidates, File.Exists);
    }

    /// <summary>
    /// Relaunches this executable elevated (UAC prompt) with the given arguments and waits.
    /// Returns false when the user declined elevation.
    /// </summary>
    public (bool Started, int ExitCode) RunElevatedVerb(string arguments)
    {
        var exe = Environment.ProcessPath
                  ?? throw new InvalidOperationException("Cannot determine the app executable path.");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        };
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return (false, -1);
            process.WaitForExit();
            return (true, process.ExitCode);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC prompt declined.
            return (false, -1);
        }
    }

    // ----- Elevated operations (executed inside the elevated relaunch) -----

    public static (int ExitCode, string Log) InstallService(string serviceExePath)
    {
        var log = new StringBuilder();

        // Idempotent reinstall: ignore failures for the not-yet-installed case.
        RunSc(log, "stop", ServiceConstants.ServiceName);
        RunSc(log, "delete", ServiceConstants.ServiceName);

        var create = RunSc(log, "create", ServiceConstants.ServiceName,
            "binPath=", Quote(serviceExePath),
            "start=", "auto",
            "DisplayName=", ServiceConstants.DisplayName);
        if (create != 0)
            return (create, log.ToString());

        RunSc(log, "description", ServiceConstants.ServiceName, ServiceConstants.Description);
        // Restart automatically on crashes: twice after 1 minute, then after 5 minutes.
        RunSc(log, "failure", ServiceConstants.ServiceName,
            "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/300000");

        var start = RunSc(log, "start", ServiceConstants.ServiceName);
        return (start, log.ToString());
    }

    public static (int ExitCode, string Log) UninstallService()
    {
        var log = new StringBuilder();
        RunSc(log, "stop", ServiceConstants.ServiceName);
        var delete = RunSc(log, "delete", ServiceConstants.ServiceName);
        return (delete, log.ToString());
    }

    public static (int ExitCode, string Log) StartService()
    {
        var log = new StringBuilder();
        return (RunSc(log, "start", ServiceConstants.ServiceName), log.ToString());
    }

    public static (int ExitCode, string Log) StopService()
    {
        var log = new StringBuilder();
        return (RunSc(log, "stop", ServiceConstants.ServiceName), log.ToString());
    }

    /// <summary>sc.exe wants values quoted when they contain spaces (e.g. binPath).</summary>
    private static string Quote(string value) => "\"" + value + "\"";

    private static int RunSc(StringBuilder log, params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        log.Append("sc ").AppendJoin(' ', arguments).AppendLine();
        try
        {
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            log.AppendLine(output.Trim());
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            log.AppendLine(ex.Message);
            return -1;
        }
    }
}
