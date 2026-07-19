using System.Collections.Concurrent;
using SqlBackup.Core.Backup;
using SqlBackup.Core.Ipc;
using SqlBackup.Core.Models;
using SqlBackup.Core.Monitoring;
using SqlBackup.Core.Notifications;
using SqlBackup.Core.Scheduling;
using SqlBackup.Core.Security;

namespace SqlBackup.Service;

/// <summary>
/// The backup engine's main loop: keeps per-job next-run times, fires due jobs
/// (never overlapping runs of the same job), and handles config hot-reload and
/// missed-run catch-up after a service restart.
/// </summary>
public sealed class SchedulerWorker : BackgroundService
{
    private readonly ServiceState _state;
    private readonly JobRunner _runner;
    private readonly ILogger<SchedulerWorker> _log;
    private readonly AlertDispatcher _alerts;
    private readonly ConcurrentDictionary<Guid, Task> _running = new();
    private readonly Dictionary<string, DateTimeOffset> _rpoLastAlerted = new();
    private DateTimeOffset _lastRpoCheck = DateTimeOffset.MinValue;
    private CancellationToken _stoppingToken = CancellationToken.None;

    public SchedulerWorker(ServiceState state, JobRunner runner, ISecretProtector protector, ILogger<SchedulerWorker> log)
    {
        _state = state;
        _runner = runner;
        _log = log;
        _alerts = new AlertDispatcher(log, protector);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        _log.LogInformation("Backup engine starting (pid {Pid}, data dir {DataDir})",
            Environment.ProcessId, SqlBackup.Core.AppPaths.DataDir);

        _state.ReloadIfChanged(force: true);
        if (_state.ConfigError is { } error)
            _log.LogError("Configuration could not be loaded: {Error}", error);

        InitializeNextRuns(allowCatchUp: true);
        _state.ConfigReloaded += OnConfigReloaded;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _state.ReloadIfChanged();
                    FireDueJobs();
                    await CheckRpoAsync();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Scheduler loop iteration failed");
                }

                var poll = Math.Clamp(_state.Config.Service.SchedulerPollSeconds, 1, 60);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(poll), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
        finally
        {
            _state.ConfigReloaded -= OnConfigReloaded;
            await WaitForRunningJobsAsync();
            _log.LogInformation("Backup engine stopped");
        }
    }

    private void OnConfigReloaded()
    {
        _log.LogInformation("Configuration reloaded (modified {Stamp:u})", _state.Config.ModifiedUtc);
        InitializeNextRuns(allowCatchUp: false);
    }

    private void FireDueJobs()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var job in _state.Config.Jobs.Where(j => j.Enabled))
        {
            var due = _state.WithRuntime(job.Id, rt =>
                !rt.IsRunning && rt.NextRunUtc is { } next && next <= now ? next : (DateTimeOffset?)null);
            if (due is null)
                continue;

            // Well overdue == the occurrence was missed while the service was down.
            var trigger = now - due.Value > TimeSpan.FromMinutes(2) ? RunTrigger.CatchUp : RunTrigger.Scheduled;
            StartJob(job, trigger);
        }
    }

    /// <summary>Computes next-run times for every job; called at startup and after each config change.</summary>
    private void InitializeNextRuns(bool allowCatchUp)
    {
        Dictionary<Guid, JobHistoryEntry> lastEntries;
        try
        {
            lastEntries = _state.History.GetLastEntryPerJob();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read job history; scheduling from now");
            lastEntries = new Dictionary<Guid, JobHistoryEntry>();
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var job in _state.Config.Jobs)
        {
            var (next, scheduleError, skipped) = _state.WithRuntime(job.Id, rt =>
            {
                rt.LastRunUtc ??= lastEntries.TryGetValue(job.Id, out var last) ? last.StartedUtc : null;
                if (rt.LastRunSuccess is null && lastEntries.TryGetValue(job.Id, out var lastEntry))
                    rt.LastRunSuccess = lastEntry.Success;

                if (rt.IsRunning)
                    return (null, null, Skipped: true); // recomputed when the current run finishes

                if (!job.Enabled)
                {
                    rt.NextRunUtc = null;
                    rt.ScheduleError = null;
                    return (null, null, Skipped: true);
                }

                rt.NextRunUtc = ComputeNextRun(job, rt.LastRunUtc, now, allowCatchUp, out var error);
                rt.ScheduleError = error;
                return (rt.NextRunUtc, error, Skipped: false);
            });

            if (skipped)
                continue;
            if (scheduleError is not null)
                _log.LogError("Job '{Job}' cannot be scheduled: {Error}", job.Name, scheduleError);
            else
                _log.LogInformation("Job '{Job}' next run: {Next:u}", job.Name, next);
        }
    }

    /// <summary>
    /// Next occurrence for a job. With <paramref name="allowCatchUp"/> and the job's
    /// CatchUpMissedRun flag, a missed occurrence (service was down) yields a time in
    /// the past, which fires immediately — exactly one catch-up run.
    /// </summary>
    public static DateTimeOffset? ComputeNextRun(
        BackupJob job,
        DateTimeOffset? lastRun,
        DateTimeOffset now,
        bool allowCatchUp,
        out string? scheduleError)
    {
        scheduleError = null;
        try
        {
            var tz = TimeZoneInfo.Local;
            if (job.Schedule.Kind == ScheduleKind.Interval)
                return ScheduleCalculator.GetNextOccurrence(job.Schedule, now, lastRun, tz);

            var from = allowCatchUp && job.CatchUpMissedRun && lastRun is { } last && last < now ? last : now;
            return ScheduleCalculator.GetNextOccurrence(job.Schedule, from, lastRun, tz);
        }
        catch (Exception ex)
        {
            scheduleError = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Periodic RPO (missing-backup) evaluation. Breaches surface in get-status and
    /// alert on every enabled channel — once on detection, again every 24h while
    /// the breach persists.
    /// </summary>
    private async Task CheckRpoAsync()
    {
        var config = _state.Config;
        var interval = TimeSpan.FromMinutes(Math.Clamp(config.Service.RpoCheckMinutes, 1, 24 * 60));
        var now = DateTimeOffset.UtcNow;
        if (now - _lastRpoCheck < interval)
            return;
        _lastRpoCheck = now;

        if (!config.Jobs.Any(j => j.RpoHours > 0))
        {
            _state.SetRpoBreaches(new List<string>());
            return;
        }

        var breaches = RpoMonitor.Evaluate(
            config.Jobs,
            _state.History.GetLastSuccessPerJob(),
            _state.History.GetLastSuccessPerJobDatabase(),
            now);
        _state.SetRpoBreaches(breaches.Select(b => b.Describe()).ToList());

        var toAlert = new List<RpoBreach>();
        foreach (var breach in breaches)
        {
            if (!_rpoLastAlerted.TryGetValue(breach.Key, out var lastAlert) || now - lastAlert > TimeSpan.FromHours(24))
            {
                toAlert.Add(breach);
                _rpoLastAlerted[breach.Key] = now;
            }
        }
        // Forget resolved breaches so they re-alert immediately if they come back.
        foreach (var gone in _rpoLastAlerted.Keys.Except(breaches.Select(b => b.Key)).ToList())
            _rpoLastAlerted.Remove(gone);

        foreach (var breach in breaches)
            _log.LogWarning("RPO breach: {Breach}", breach.Describe());
        if (toAlert.Count > 0)
            await _alerts.SendRpoBreachesAsync(config.Notifications, toAlert, _stoppingToken);
    }

    /// <summary>Manual trigger, called by the IPC server.</summary>
    public RunJobResponse TriggerManualRun(Guid jobId)
    {
        var job = _state.Config.FindJob(jobId);
        if (job is null)
            return new RunJobResponse { Accepted = false, Reason = "Unknown job id — save the job first and reload the service config." };
        if (_state.WithRuntime(jobId, rt => rt.IsRunning))
            return new RunJobResponse { Accepted = false, Reason = $"Job '{job.Name}' is already running." };

        StartJob(job, RunTrigger.Manual);
        return new RunJobResponse { Accepted = true };
    }

    private void StartJob(BackupJob job, RunTrigger trigger)
    {
        if (!_state.TryBeginRun(job.Id))
            return;

        var startedUtc = DateTimeOffset.UtcNow;
        _state.WithRuntime(job.Id, rt => rt.NextRunUtc = null); // shown as "running" until the run completes

        var connection = _state.Config.FindConnection(job.ConnectionId);
        var task = Task.Run(async () =>
        {
            var summary = "FAILED (internal error)";
            var success = false;
            try
            {
                if (connection is null)
                {
                    summary = "FAILED (connection profile not found)";
                    foreach (var db in job.Databases.DefaultIfEmpty("(none)"))
                    {
                        _state.History.Append(new JobHistoryEntry
                        {
                            JobId = job.Id,
                            JobName = job.Name,
                            Database = db,
                            Type = job.Type,
                            Trigger = trigger,
                            StartedUtc = startedUtc,
                            Success = false,
                            Error = "The job references a connection profile that no longer exists.",
                        });
                    }
                }
                else
                {
                    var config = _state.Config;
                    var result = await _runner.RunAsync(new JobRunContext
                    {
                        Job = job,
                        Connection = connection,
                        Trigger = trigger,
                        Settings = config.Service,
                        Notifications = config.Notifications,
                        OffsiteDestination = config.FindOffsiteDestination(job.OffsiteDestinationId),
                    }, _stoppingToken);
                    success = result.AllSucceeded;
                    summary = result.Summarize();
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Job '{Job}' crashed", job.Name);
                summary = $"FAILED ({ex.Message})";
            }
            finally
            {
                _state.EndRun(job.Id, startedUtc, success, summary);
                RecomputeAfterRun(job.Id, startedUtc);
                _running.TryRemove(job.Id, out _);
            }
        });
        _running[job.Id] = task;
    }

    private void RecomputeAfterRun(Guid jobId, DateTimeOffset startedUtc)
    {
        // Re-resolve the job: it may have been edited or deleted while running.
        var job = _state.Config.FindJob(jobId);
        _state.WithRuntime(jobId, rt =>
        {
            if (job is null || !job.Enabled)
            {
                rt.NextRunUtc = null;
                return;
            }
            rt.NextRunUtc = ComputeNextRun(job, startedUtc, DateTimeOffset.UtcNow, allowCatchUp: false, out var error);
            rt.ScheduleError = error;
        });
    }

    private async Task WaitForRunningJobsAsync()
    {
        var pending = _running.Values.ToArray();
        if (pending.Length == 0)
            return;
        _log.LogInformation("Waiting for {Count} running job(s) to finish...", pending.Length);
        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            _log.LogWarning("Not all jobs finished before shutdown: {Error}", ex.Message);
        }
    }
}
