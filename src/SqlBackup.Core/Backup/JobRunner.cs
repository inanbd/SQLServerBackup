using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlBackup.Core.History;
using SqlBackup.Core.Models;
using SqlBackup.Core.Notifications;
using SqlBackup.Core.Security;

namespace SqlBackup.Core.Backup;

public sealed class JobRunContext
{
    public required BackupJob Job { get; init; }
    public required ConnectionProfile Connection { get; init; }
    public required RunTrigger Trigger { get; init; }
    public required ServiceSettings Settings { get; init; }
    /// <summary>When set, an email report is sent after the run according to these settings.</summary>
    public NotificationSettings? Notifications { get; init; }
}

public sealed class JobRunResult
{
    public List<JobHistoryEntry> Entries { get; } = new();
    public bool AllSucceeded => Entries.Count > 0 && Entries.All(e => e.Success);
    public int FailureCount => Entries.Count(e => !e.Success);

    public string Summarize()
    {
        if (Entries.Count == 0)
            return "Nothing to do";
        return AllSucceeded
            ? $"OK ({Entries.Count} database(s))"
            : $"FAILED ({FailureCount} of {Entries.Count} database(s))";
    }
}

/// <summary>
/// Executes one backup job end to end: connect (with retry/backoff), preflight,
/// per-database BACKUP + optional VERIFYONLY, retention, history and notification.
/// Used by both the Windows service and the desktop app's standalone mode.
/// </summary>
public sealed class JobRunner
{
    private readonly ILogger _log;
    private readonly HistoryStore _history;
    private readonly ISecretProtector _protector;

    public JobRunner(ILogger log, HistoryStore history, ISecretProtector protector)
    {
        _log = log;
        _history = history;
        _protector = protector;
    }

    public async Task<JobRunResult> RunAsync(JobRunContext ctx, CancellationToken ct = default)
    {
        var job = ctx.Job;
        var result = new JobRunResult();
        _log.LogInformation("Job '{Job}' starting ({Trigger}, {Count} database(s))", job.Name, ctx.Trigger, job.Databases.Count);

        if (job.Databases.Count == 0)
        {
            RecordFailure(result, ctx, "(none)", "The job has no databases selected.");
            return result;
        }

        string connectionString;
        try
        {
            connectionString = SqlConnectionFactory.BuildConnectionString(ctx.Connection, _protector);
        }
        catch (Exception ex)
        {
            foreach (var db in job.Databases)
                RecordFailure(result, ctx, db, $"Connection configuration error: {ex.Message}");
            await NotifyAsync(ctx, result);
            return result;
        }

        var (reachable, connectError) = await TryConnectWithRetryAsync(
            connectionString, ctx.Settings, ctx.Connection.AuthMode, ct);
        if (!reachable)
        {
            foreach (var db in job.Databases)
                RecordFailure(result, ctx, db, connectError ?? "SQL Server unreachable.");
            await NotifyAsync(ctx, result);
            return result;
        }

        var destinationIssues = PreflightChecker.CheckDestination(
            job.DestinationFolder, expectedBytes: null, ctx.Settings.MinFreeDiskSpaceWarnMb);

        foreach (var database in job.Databases)
        {
            ct.ThrowIfCancellationRequested();
            var entry = await BackupOneDatabaseAsync(ctx, connectionString, database, destinationIssues, ct);
            _history.Append(entry);
            result.Entries.Add(entry);
        }

        await NotifyAsync(ctx, result);
        _log.LogInformation("Job '{Job}' finished: {Summary}", job.Name, result.Summarize());
        return result;
    }

    private async Task<JobHistoryEntry> BackupOneDatabaseAsync(
        JobRunContext ctx,
        string connectionString,
        string database,
        IReadOnlyList<PreflightIssue> destinationIssues,
        CancellationToken ct)
    {
        var job = ctx.Job;
        var entry = new JobHistoryEntry
        {
            JobId = job.Id,
            JobName = job.Name,
            Database = database,
            Type = job.Type,
            Trigger = ctx.Trigger,
            StartedUtc = DateTimeOffset.UtcNow,
        };
        var notes = destinationIssues.Select(i => i.Message).ToList();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (destinationIssues.Any(i => i.IsFatal))
                throw new InvalidOperationException(destinationIssues.First(i => i.IsFatal).Message);

            var effectiveType = job.Type;

            // Chain awareness: differentials and log backups need a base full backup.
            if (effectiveType != BackupType.Full &&
                !await SqlServerQueries.HasFullBackupAsync(connectionString, database, ct))
            {
                if (job.Options.FallbackToFullIfNoBase)
                {
                    effectiveType = BackupType.Full;
                    notes.Add($"No full backup exists for '{database}' yet — took a FULL backup instead of {job.Type}.");
                }
                else
                {
                    throw new InvalidOperationException(
                        $"No full backup exists for '{database}'. A {job.Type} backup requires a base full backup.");
                }
            }

            if (effectiveType == BackupType.TransactionLog)
            {
                var recoveryModel = await SqlServerQueries.GetRecoveryModelAsync(connectionString, database, ct);
                if (string.Equals(recoveryModel, "SIMPLE", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Database '{database}' uses the SIMPLE recovery model; transaction log backups are not possible. " +
                        "Switch the job to Full/Differential or change the recovery model.");
            }

            var folder = job.SubfolderPerDatabase
                ? Path.Combine(job.DestinationFolder, BackupFileNamer.SanitizeForFileName(database))
                : job.DestinationFolder;
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch
            {
                // Path may only be resolvable by the SQL Server host; the BACKUP command decides.
            }

            var now = DateTime.Now;
            var filePath = Path.Combine(folder, BackupFileNamer.BuildFileName(database, effectiveType, now));
            var script = BackupScriptBuilder.BuildBackupCommand(database, effectiveType, filePath, job.Options, now);

            _log.LogInformation("Backing up [{Database}] ({Type}) to {File}", database, effectiveType, filePath);
            var execution = await BackupExecutor.ExecuteAsync(
                connectionString, script, ctx.Settings.CommandTimeoutSeconds, ct);

            var summary = execution.Messages.LastOrDefault(m => m.Contains("successfully processed", StringComparison.OrdinalIgnoreCase));
            if (summary is not null)
                notes.Add(summary);

            if (job.Options.VerifyAfterBackup)
            {
                await BackupExecutor.ExecuteAsync(
                    connectionString,
                    BackupScriptBuilder.BuildVerifyCommand(filePath, job.Options.Checksum),
                    ctx.Settings.CommandTimeoutSeconds, ct);
                notes.Add("RESTORE VERIFYONLY passed.");
            }

            entry.Type = effectiveType;
            entry.FilePath = filePath;

            if (File.Exists(filePath))
            {
                entry.FileSizeBytes = new FileInfo(filePath).Length;
                var retention = RetentionEnforcer.Apply(folder, database, effectiveType, job.Retention, now);
                if (retention.DeletedFiles.Count > 0)
                    notes.Add($"Retention: deleted {retention.DeletedFiles.Count} old backup file(s).");
                foreach (var error in retention.Errors)
                    notes.Add($"Retention error: {error}");
            }
            else
            {
                notes.Add("Backup file is not visible from this machine (it was written by the SQL Server host); " +
                          "size not recorded and retention skipped.");
            }

            entry.Success = true;
        }
        catch (OperationCanceledException)
        {
            entry.Success = false;
            entry.Error = "The backup was cancelled (service stopping or request aborted).";
        }
        catch (SqlException ex)
        {
            entry.Success = false;
            entry.Error = BuildSqlErrorText(ex, ctx.Connection.AuthMode);
            _log.LogError(ex, "Backup of [{Database}] failed", database);
        }
        catch (Exception ex)
        {
            entry.Success = false;
            entry.Error = ex.Message;
            _log.LogError(ex, "Backup of [{Database}] failed", database);
        }
        finally
        {
            stopwatch.Stop();
            entry.DurationSeconds = stopwatch.Elapsed.TotalSeconds;
            if (notes.Count > 0)
                entry.Message = string.Join(" ", notes);
        }

        return entry;
    }

    private async Task<(bool Ok, string? Error)> TryConnectWithRetryAsync(
        string connectionString, ServiceSettings settings, SqlAuthMode authMode, CancellationToken ct)
    {
        var attempts = Math.Max(0, settings.SqlConnectRetries) + 1;
        string? lastError = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync(ct);
                return (true, null);
            }
            catch (SqlException ex) when (SqlErrorHints.IsLoginFailure(ex.Errors.Cast<SqlError>().Select(e => e.Number)))
            {
                // Authentication failures won't heal with retries — fail immediately with guidance.
                _log.LogError(ex, "SQL Server sign-in failed");
                return (false, $"Could not sign in to SQL Server: {BuildSqlErrorText(ex, authMode)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex.Message;
                if (attempt < attempts - 1)
                {
                    var delay = TimeSpan.FromSeconds(
                        Math.Max(1, settings.SqlRetryBaseDelaySeconds) * Math.Pow(3, attempt));
                    _log.LogWarning("SQL Server not reachable (attempt {Attempt}/{Total}): {Error}. Retrying in {Delay}s",
                        attempt + 1, attempts, ex.Message, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
            }
        }

        return (false, $"SQL Server unreachable after {attempts} attempt(s): {lastError}");
    }

    /// <summary>Joins the distinct SQL error messages and appends identity/permission guidance when relevant.</summary>
    internal static string BuildSqlErrorText(SqlException ex, SqlAuthMode authMode)
    {
        var errors = ex.Errors.Cast<SqlError>().ToList();
        var text = string.Join(" | ", errors.Select(e => e.Message).Distinct());
        var hint = SqlErrorHints.ForBackupFailure(
            errors.Select(e => e.Number).ToArray(), authMode, SqlErrorHints.CurrentProcessAccount);
        return hint is null ? text : $"{text} — {hint}";
    }

    private void RecordFailure(JobRunResult result, JobRunContext ctx, string database, string error)
    {
        var entry = new JobHistoryEntry
        {
            JobId = ctx.Job.Id,
            JobName = ctx.Job.Name,
            Database = database,
            Type = ctx.Job.Type,
            Trigger = ctx.Trigger,
            StartedUtc = DateTimeOffset.UtcNow,
            Success = false,
            Error = error,
        };
        _history.Append(entry);
        result.Entries.Add(entry);
        _log.LogError("Job '{Job}' / [{Database}]: {Error}", ctx.Job.Name, database, error);
    }

    private async Task NotifyAsync(JobRunContext ctx, JobRunResult result)
    {
        if (ctx.Notifications is null || result.Entries.Count == 0)
            return;
        try
        {
            await EmailNotifier.SendJobReportAsync(ctx.Notifications, _protector, ctx.Job.Name, result.Entries);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Sending the notification email failed");
        }
    }
}
