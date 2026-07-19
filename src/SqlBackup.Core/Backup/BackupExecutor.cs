using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SqlBackup.Core.Backup;

public sealed class BackupExecutionResult
{
    public TimeSpan Duration { get; init; }
    /// <summary>Server info messages ("10 percent processed.", "BACKUP DATABASE successfully processed ...").</summary>
    public List<string> Messages { get; init; } = new();
}

/// <summary>Runs a backup/verify batch and captures the server's progress messages.</summary>
public static class BackupExecutor
{
    public static async Task<BackupExecutionResult> ExecuteAsync(
        string connectionString,
        string script,
        int commandTimeoutSeconds,
        CancellationToken ct = default,
        Action<string>? onMessage = null)
    {
        var messages = new List<string>();

        await using var conn = new SqlConnection(connectionString);
        conn.InfoMessage += (_, e) =>
        {
            foreach (SqlError error in e.Errors)
            {
                messages.Add(error.Message);
                onMessage?.Invoke(error.Message);
            }
        };

        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(script, conn)
        {
            // 0 means "no timeout" for SqlCommand, which is what long-running backups need.
            CommandTimeout = Math.Max(0, commandTimeoutSeconds),
        };

        var stopwatch = Stopwatch.StartNew();
        await cmd.ExecuteNonQueryAsync(ct);
        stopwatch.Stop();

        return new BackupExecutionResult { Duration = stopwatch.Elapsed, Messages = messages };
    }
}
