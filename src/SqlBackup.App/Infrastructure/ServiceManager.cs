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

    /// <summary>
    /// Stable machine-wide home for the service binaries. Services must not run from
    /// per-user or ClickOnce cache paths, so installation copies the files here first.
    /// </summary>
    public static string StableServiceDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SqlBackup", "Service");

    /// <summary>Locates SqlBackup.Service.exe next to the app (installer layout), in the dev tree, or the stable copy.</summary>
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
            // A previously installed stable copy (lets reinstall work when the source moved away).
            Path.Combine(StableServiceDir, "SqlBackup.Service.exe"),
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

        // Preflight: the exe alone cannot run — its application files must sit next to it.
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(serviceExePath))!;
        if (!File.Exists(serviceExePath))
            return (2, $"Service executable not found: {serviceExePath}");
        if (!File.Exists(Path.Combine(sourceDir, "SqlBackup.Service.dll")))
        {
            return (2,
                $"'{serviceExePath}' is missing its application files (SqlBackup.Service.dll is not next to it), " +
                "so the service process cannot start. Use the service's full build/publish output folder — " +
                "for example: dotnet publish src/SqlBackup.Service -c Release -r win-x64 --self-contained true");
        }

        // Idempotent reinstall: ignore failures for the not-yet-installed case.
        RunSc(log, "stop", ServiceConstants.ServiceName);
        RunSc(log, "delete", ServiceConstants.ServiceName);

        // Never register a service inside a user profile or ClickOnce cache — those paths
        // are per-user, change between publishes, and vanish on profile cleanup. Copy the
        // whole service folder to a stable machine-wide location and register that copy.
        var installExe = serviceExePath;
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(sourceDir),
                Path.TrimEndingDirectorySeparator(StableServiceDir),
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                log.AppendLine($"Copying service files to {StableServiceDir}");
                CopyDirectory(sourceDir, StableServiceDir);
                installExe = Path.Combine(StableServiceDir, Path.GetFileName(serviceExePath));
            }
            catch (Exception ex)
            {
                return (3, log + $"Could not copy the service to {StableServiceDir}: {ex.Message}");
            }
        }

        var create = RunSc(log, "create", ServiceConstants.ServiceName,
            "binPath=", Quote(installExe),
            "start=", "auto",
            "DisplayName=", ServiceConstants.DisplayName);
        if (create != 0)
            return (create, log.ToString());

        RunSc(log, "description", ServiceConstants.ServiceName, ServiceConstants.Description);
        // Restart automatically on crashes: twice after 1 minute, then after 5 minutes.
        RunSc(log, "failure", ServiceConstants.ServiceName,
            "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/300000");

        var start = RunSc(log, "start", ServiceConstants.ServiceName);
        if (start != 0)
        {
            log.AppendLine();
            log.AppendLine("The service was registered but failed to start (error 1053 = the process never " +
                           "reported in). Most common causes:");
            log.AppendLine("- Incomplete service files: reinstall from a full publish output " +
                           "(dotnet publish -c Release -r win-x64 --self-contained true).");
            log.AppendLine("- Missing .NET 8 runtime for a framework-dependent build (self-contained avoids this).");
            log.AppendLine($"- Startup crash: check {SqlBackup.Core.AppPaths.LogDir} for service-*.log — " +
                           "if none was written, the process never launched. Windows Event Viewer " +
                           "(Windows Logs → Application) may have the loader error.");
        }
        return (start, log.ToString());
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
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
