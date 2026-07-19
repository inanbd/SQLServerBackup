namespace SqlBackup.Core;

/// <summary>
/// Well-known on-disk locations shared by the service and the desktop app.
/// Defaults to %ProgramData%\SqlBackup; override with the SQLBACKUP_DATA_DIR
/// environment variable (used by tests and non-Windows development).
/// </summary>
public static class AppPaths
{
    public const string DataDirEnvVar = "SQLBACKUP_DATA_DIR";

    public static string DataDir =>
        Environment.GetEnvironmentVariable(DataDirEnvVar) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SqlBackup");

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string HistoryDir => Path.Combine(DataDir, "history");
    public static string LogDir => Path.Combine(DataDir, "logs");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(HistoryDir);
        Directory.CreateDirectory(LogDir);
    }
}
