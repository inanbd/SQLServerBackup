using System;

namespace SqlBackup.App.Infrastructure;

/// <summary>In-app event hub (dashboard polling -> tray balloon notifications).</summary>
public static class AppEvents
{
    public static event Action<string>? BackupFailureDetected;

    public static void RaiseBackupFailure(string message) => BackupFailureDetected?.Invoke(message);
}
