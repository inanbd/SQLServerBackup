using System.Text.Json.Serialization;

namespace SqlBackup.Core.Models;

/// <summary>One backup attempt for one database. Persisted as JSON lines by the history store.</summary>
public sealed class JobHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }

    public string JobName { get; set; } = "";

    public string Database { get; set; } = "";

    /// <summary>The backup type actually taken (may differ from the job type after a full-backup fallback).</summary>
    public BackupType Type { get; set; }

    public RunTrigger Trigger { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public double DurationSeconds { get; set; }

    public bool Success { get; set; }

    public string? FilePath { get; set; }

    public long? FileSizeBytes { get; set; }

    /// <summary>Informational notes: fallback taken, verification result, retention actions, warnings.</summary>
    public string? Message { get; set; }

    public string? Error { get; set; }

    /// <summary>Outcome of the off-site copy: null = not configured/not applicable.</summary>
    public bool? OffsiteSuccess { get; set; }

    /// <summary>Backup succeeded AND the off-site copy (when configured) succeeded.</summary>
    [JsonIgnore]
    public bool IsFullySuccessful => Success && OffsiteSuccess != false;
}
