using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SqlBackup.Core.History;
using SqlBackup.Core.Models;
using SqlBackup.Core.Notifications;
using SqlBackup.Core.Offsite;
using SqlBackup.Core.Security;

namespace SqlBackup.Core.Backup;

public sealed class JobRunContext
{
    public required BackupJob Job { get; init; }
    public required ConnectionProfile Connection { get; init; }
    public required RunTrigger Trigger { get; init; }
    public required ServiceSettings Settings { get; init; }
    /// <summary>When set, alerts (email/webhook/event log) are sent after the run.</summary>
    public NotificationSettings? Notifications { get; init; }
    /// <summary>Resolved off-site destination for the job, when configured.</summary>
    public OffsiteDestination? OffsiteDestination { get; init; }
}

public sealed class JobRunResult
{
    public List<JobHistoryEntry> Entries { get; } = new();
    public bool AllSucceeded => Entries.Count > 0 && Entries.All(e => e.IsFullySuccessful);
    public int BackupFailureCount => Entries.Count(e => !e.Success);
    public int OffsiteFailureCount => Entries.Count(e => e.Success && e.OffsiteSuccess == false);

    public string Summarize()
    {
        if (Entries.Count == 0)
            return "Nothing to do";
        if (BackupFailureCount > 0)
            return $"FAILED ({BackupFailureCount} of {Entries.Count} database(s))";
        return OffsiteFailureCount > 0
            ? $"OK, but off-site copy failed for {OffsiteFailureCount} of {Entries.Count} database(s)"
            : $"OK ({Entries.Count} database(s))";
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
    private readonly AlertDispatcher _alerts;

    public JobRunner(ILogger log, HistoryStore history, ISecretProtector protector)
    {
        _log = log;
        _history = history;
        _protector = protector;
        _alerts = new AlertDispatcher(log, protector);
    }

    public async Task<JobRunResult> RunAsync(JobRunContext ctx, CancellationToken ct = default)
    {
        var job = ctx.Job;
        var result = new JobRunResult();
        _log.LogInformation("Job '{Job}' starting ({Trigger}, targets: {Targets})",
            job.Name, ctx.Trigger, DatabaseSelector.Describe(job));

        if (job.SelectionMode == DatabaseSelectionMode.Explicit && job.Databases.Count == 0)
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
            RecordFailureForAllTargets(result, ctx, $"Connection configuration error: {ex.Message}");
            await NotifyAsync(ctx, result);
            return result;
        }

        var (reachable, connectError) = await TryConnectWithRetryAsync(
            connectionString, ctx.Settings, ctx.Connection.AuthMode, ct);
        if (!reachable)
        {
            RecordFailureForAllTargets(result, ctx, connectError ?? "SQL Server unreachable.");
            await NotifyAsync(ctx, result);
            return result;
        }

        // Discovery modes resolve their database list at run time, so databases
        // created after the job was configured are picked up automatically.
        List<string> databases;
        try
        {
            List<string>? onServer = null;
            if (job.SelectionMode != DatabaseSelectionMode.Explicit)
            {
                onServer = await SqlServerQueries.ListDatabasesAsync(
                    connectionString, includeSystemDatabases: job.SelectionMode == DatabaseSelectionMode.AllDatabases, ct);
            }
            databases = DatabaseSelector.Resolve(job.SelectionMode, job.Databases, job.ExcludedDatabases, onServer);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RecordFailureForAllTargets(result, ctx, $"Could not enumerate databases: {ex.Message}");
            await NotifyAsync(ctx, result);
            return result;
        }

        if (databases.Count == 0)
        {
            RecordFailure(result, ctx, "(none)", "No databases matched the job's selection (check exclusions).");
            await NotifyAsync(ctx, result);
            return result;
        }

        var destinationIssues = PreflightChecker.CheckDestination(
            job.DestinationFolder, expectedBytes: null, ctx.Settings.MinFreeDiskSpaceWarnMb);

        using var offsite = CreateOffsiteUploader(ctx, result);

        foreach (var database in databases)
        {
            ct.ThrowIfCancellationRequested();
            var entry = await BackupOneDatabaseAsync(ctx, connectionString, database, destinationIssues, offsite?.Uploader, ct);
            _history.Append(entry);
            result.Entries.Add(entry);
        }

        await NotifyAsync(ctx, result);
        _log.LogInformation("Job '{Job}' finished: {Summary}", job.Name, result.Summarize());
        return result;
    }

    private sealed class OffsiteHandle : IDisposable
    {
        public required Offsite.IOffsiteProvider Provider { get; init; }
        public required OffsiteUploader Uploader { get; init; }
        public void Dispose() => Provider.Dispose();
    }

    private OffsiteHandle? CreateOffsiteUploader(JobRunContext ctx, JobRunResult result)
    {
        if (ctx.Job.StorageMode == BackupStorageMode.LocalOnly || ctx.OffsiteDestination is not { } destination)
            return null;
        try
        {
            var provider = OffsiteProviderFactory.Create(destination, _protector);
            return new OffsiteHandle { Provider = provider, Uploader = new OffsiteUploader(provider, _log) };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Off-site destination '{Name}' is unusable", destination.Name);
            // Recorded per entry below via the null uploader + note is not possible here;
            // surface it once as a job-level marker on the first entry instead.
            result.Entries.Add(new JobHistoryEntry
            {
                JobId = ctx.Job.Id,
                JobName = ctx.Job.Name,
                Database = "(off-site)",
                Type = ctx.Job.Type,
                Trigger = ctx.Trigger,
                StartedUtc = DateTimeOffset.UtcNow,
                Success = false,
                Error = $"Off-site destination '{destination.Name}' is unusable: {ex.Message}",
            });
            _history.Append(result.Entries[^1]);
            return null;
        }
    }

    private async Task<JobHistoryEntry> BackupOneDatabaseAsync(
        JobRunContext ctx,
        string connectionString,
        string database,
        IReadOnlyList<PreflightIssue> destinationIssues,
        OffsiteUploader? offsite,
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

                // Off-site-only treats the destination folder as staging; with no usable
                // off-site target it degrades to keeping the local copy, never to none.
                var keepsLocal = BackupJob.KeepsLocalCopy(job.StorageMode, offsite is not null);

                if (keepsLocal)
                {
                    var retention = RetentionEnforcer.Apply(folder, database, effectiveType, job.Retention, now);
                    if (retention.DeletedFiles.Count > 0)
                        notes.Add($"Retention: deleted {retention.DeletedFiles.Count} old backup file(s).");
                    if (retention.KeptForChain.Count > 0)
                        notes.Add($"Retention: kept {retention.KeptForChain.Count} full backup(s) still needed by newer differential/log backups.");
                    foreach (var error in retention.Errors)
                        notes.Add($"Retention error: {error}");
                }

                if (offsite is not null)
                {
                    var offsiteResult = await offsite.ProcessAsync(
                        filePath, database, job.SubfolderPerDatabase, effectiveType,
                        job.OffsiteRetention, ctx.OffsiteDestination?.Prefix, now, ct);
                    entry.OffsiteSuccess = offsiteResult.Success;
                    notes.AddRange(offsiteResult.Notes);

                    if (!keepsLocal && offsiteResult.Success)
                    {
                        try
                        {
                            File.Delete(filePath);
                            notes.Add("Off-site only: the local staging copy was deleted after the upload succeeded.");
                        }
                        catch (Exception ex)
                        {
                            notes.Add($"Off-site only: the local staging copy could not be deleted: {ex.Message}");
                        }
                    }
                    else if (!keepsLocal)
                    {
                        notes.Add("Off-site only: the local copy was KEPT because the upload failed — " +
                                  "it is currently the only copy of this backup.");
                    }
                }
            }
            else
            {
                notes.Add("Backup file is not visible from this machine (it was written by the SQL Server host); " +
                          "size not recorded, retention and off-site copy skipped.");
                if (offsite is not null)
                    entry.OffsiteSuccess = false;
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

    /// <summary>One failure entry per known target; discovery modes get a single descriptive entry.</summary>
    private void RecordFailureForAllTargets(JobRunResult result, JobRunContext ctx, string error)
    {
        if (ctx.Job.SelectionMode == DatabaseSelectionMode.Explicit)
        {
            foreach (var db in ctx.Job.Databases.DefaultIfEmpty("(none)"))
                RecordFailure(result, ctx, db, error);
        }
        else
        {
            RecordFailure(result, ctx, DatabaseSelector.Describe(ctx.Job), error);
        }
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
        await _alerts.SendJobReportAsync(ctx.Notifications, ctx.Job.Name, result.Entries);
    }
}
