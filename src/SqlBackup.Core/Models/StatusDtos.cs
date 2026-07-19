namespace SqlBackup.Core.Models;

/// <summary>Per-job runtime state reported by the service over IPC.</summary>
public sealed class JobStatusInfo
{
    public Guid JobId { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public bool IsRunning { get; set; }
    public DateTimeOffset? NextRunUtc { get; set; }
    public DateTimeOffset? LastRunUtc { get; set; }
    public bool? LastRunSuccess { get; set; }
    public string? LastRunSummary { get; set; }
    /// <summary>Set when the job cannot be scheduled (bad cron expression, missing connection).</summary>
    public string? ScheduleError { get; set; }
}

/// <summary>Snapshot of the whole service, returned by the "get-status" IPC request.</summary>
public sealed class ServiceStatusInfo
{
    public string ServiceVersion { get; set; } = "";
    public int ProcessId { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset ConfigModifiedUtc { get; set; }
    /// <summary>Set when the last attempt to load config.json failed; the service keeps the previous config.</summary>
    public string? ConfigError { get; set; }
    public List<JobStatusInfo> Jobs { get; set; } = new();
}
