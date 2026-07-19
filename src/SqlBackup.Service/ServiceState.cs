using SqlBackup.Core.Config;
using SqlBackup.Core.History;
using SqlBackup.Core.Models;

namespace SqlBackup.Service;

/// <summary>
/// Shared runtime state of the backup engine: the current config snapshot
/// (hot-reloaded when config.json changes) and per-job runtime info used to
/// answer status queries.
/// </summary>
public sealed class ServiceState
{
    public sealed class JobRuntime
    {
        public bool IsRunning { get; set; }
        public DateTimeOffset? NextRunUtc { get; set; }
        public DateTimeOffset? LastRunUtc { get; set; }
        public bool? LastRunSuccess { get; set; }
        public string? LastRunSummary { get; set; }
        public string? ScheduleError { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, JobRuntime> _runtimes = new();
    private AppConfig _config = new();
    private DateTimeOffset _configStamp = DateTimeOffset.MinValue;
    private List<string> _rpoBreaches = new();

    public ServiceState(ConfigStore store, HistoryStore history)
    {
        Store = store;
        History = history;
    }

    public ConfigStore Store { get; }
    public HistoryStore History { get; }
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;
    public string? ConfigError { get; private set; }

    /// <summary>Raised (outside the lock) after a config reload; the scheduler recomputes next runs.</summary>
    public event Action? ConfigReloaded;

    public AppConfig Config
    {
        get
        {
            lock (_gate)
            {
                return _config;
            }
        }
    }

    /// <summary>Reloads config.json when its timestamp changed. Keeps the previous config on parse errors.</summary>
    public bool ReloadIfChanged(bool force = false)
    {
        var stamp = Store.GetLastWriteUtc();
        lock (_gate)
        {
            if (!force && stamp == _configStamp)
                return false;
            try
            {
                _config = Store.Load();
                _configStamp = stamp;
                ConfigError = null;
            }
            catch (Exception ex)
            {
                ConfigError = ex.Message;
                _configStamp = stamp; // don't retry a broken file every poll tick
                return false;
            }
        }
        ConfigReloaded?.Invoke();
        return true;
    }

    /// <summary>
    /// All per-job runtime reads/writes go through these two methods so every
    /// access happens under the state lock (JobRuntime instances never escape it).
    /// </summary>
    public void WithRuntime(Guid jobId, Action<JobRuntime> action)
    {
        lock (_gate)
        {
            action(GetRuntimeUnlocked(jobId));
        }
    }

    public T WithRuntime<T>(Guid jobId, Func<JobRuntime, T> read)
    {
        lock (_gate)
        {
            return read(GetRuntimeUnlocked(jobId));
        }
    }

    public bool TryBeginRun(Guid jobId)
    {
        lock (_gate)
        {
            var runtime = GetRuntimeUnlocked(jobId);
            if (runtime.IsRunning)
                return false;
            runtime.IsRunning = true;
            return true;
        }
    }

    public void SetRpoBreaches(List<string> breaches)
    {
        lock (_gate)
        {
            _rpoBreaches = breaches;
        }
    }

    public void EndRun(Guid jobId, DateTimeOffset startedUtc, bool success, string summary)
    {
        lock (_gate)
        {
            var runtime = GetRuntimeUnlocked(jobId);
            runtime.IsRunning = false;
            runtime.LastRunUtc = startedUtc;
            runtime.LastRunSuccess = success;
            runtime.LastRunSummary = summary;
        }
    }

    public ServiceStatusInfo BuildStatus()
    {
        lock (_gate)
        {
            var status = new ServiceStatusInfo
            {
                ServiceVersion = typeof(ServiceState).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                ProcessId = Environment.ProcessId,
                ServiceAccount = Core.Backup.SqlErrorHints.CurrentProcessAccount,
                StartedUtc = StartedUtc,
                ConfigModifiedUtc = _config.ModifiedUtc,
                ConfigError = ConfigError,
                RpoBreaches = _rpoBreaches.ToList(),
            };
            foreach (var job in _config.Jobs)
            {
                var runtime = GetRuntimeUnlocked(job.Id);
                status.Jobs.Add(new JobStatusInfo
                {
                    JobId = job.Id,
                    Name = job.Name,
                    Enabled = job.Enabled,
                    IsRunning = runtime.IsRunning,
                    NextRunUtc = runtime.NextRunUtc,
                    LastRunUtc = runtime.LastRunUtc,
                    LastRunSuccess = runtime.LastRunSuccess,
                    LastRunSummary = runtime.LastRunSummary,
                    ScheduleError = runtime.ScheduleError,
                });
            }
            return status;
        }
    }

    private JobRuntime GetRuntimeUnlocked(Guid jobId)
    {
        if (!_runtimes.TryGetValue(jobId, out var runtime))
            _runtimes[jobId] = runtime = new JobRuntime();
        return runtime;
    }
}
