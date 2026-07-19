namespace SqlBackup.Core.Models;

public sealed class RetentionPolicy
{
    public RetentionMode Mode { get; set; } = RetentionMode.KeepLastN;

    /// <summary>Number of backups to keep per database (<see cref="RetentionMode.KeepLastN"/>).</summary>
    public int KeepLast { get; set; } = 14;

    /// <summary>Delete backups older than this many days (<see cref="RetentionMode.MaxAgeDays"/>).</summary>
    public int MaxAgeDays { get; set; } = 30;

    public string Describe() => Mode switch
    {
        RetentionMode.KeepAll => "Keep everything",
        RetentionMode.KeepLastN => $"Keep last {KeepLast}",
        RetentionMode.MaxAgeDays => $"Delete after {MaxAgeDays} days",
        _ => "Unknown",
    };
}

public sealed class BackupJobOptions
{
    /// <summary>Use native SQL Server backup compression (not available on Express edition).</summary>
    public bool Compression { get; set; }

    /// <summary>Write and validate page checksums during backup (WITH CHECKSUM).</summary>
    public bool Checksum { get; set; } = true;

    /// <summary>Take a copy-only backup that does not affect the backup chain (Full/Log only).</summary>
    public bool CopyOnly { get; set; }

    /// <summary>Run RESTORE VERIFYONLY against the file after each backup.</summary>
    public bool VerifyAfterBackup { get; set; } = true;

    /// <summary>
    /// If a differential or log backup is requested but the database has no full backup yet,
    /// take a full backup instead of failing.
    /// </summary>
    public bool FallbackToFullIfNoBase { get; set; } = true;
}

public sealed class BackupJob
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>References <see cref="ConnectionProfile.Id"/>.</summary>
    public Guid ConnectionId { get; set; }

    /// <summary>How the database list is determined (fixed list vs. run-time discovery).</summary>
    public DatabaseSelectionMode SelectionMode { get; set; } = DatabaseSelectionMode.Explicit;

    /// <summary>Databases to back up (<see cref="DatabaseSelectionMode.Explicit"/> only).</summary>
    public List<string> Databases { get; set; } = new();

    /// <summary>Databases to skip (All/AllUser selection modes only).</summary>
    public List<string> ExcludedDatabases { get; set; } = new();

    public BackupType Type { get; set; } = BackupType.Full;

    public ScheduleSpec Schedule { get; set; } = new();

    /// <summary>Local folder or UNC path where backup files are written.</summary>
    public string DestinationFolder { get; set; } = "";

    /// <summary>Create one subfolder per database under the destination.</summary>
    public bool SubfolderPerDatabase { get; set; } = true;

    public RetentionPolicy Retention { get; set; } = new();

    public BackupJobOptions Options { get; set; } = new();

    /// <summary>
    /// If the service was down when a scheduled occurrence should have fired,
    /// run the job once at startup instead of waiting for the next occurrence.
    /// </summary>
    public bool CatchUpMissedRun { get; set; }

    /// <summary>
    /// Recovery-point objective: alert when this job has produced no successful
    /// backup for this many hours. 0 disables the check.
    /// </summary>
    public int RpoHours { get; set; }

    /// <summary>Optional off-site copy target; references <see cref="OffsiteDestination.Id"/>.</summary>
    public Guid? OffsiteDestinationId { get; set; }

    /// <summary>Retention applied to the off-site copies (independent of local retention).</summary>
    public RetentionPolicy OffsiteRetention { get; set; } = new() { Mode = RetentionMode.KeepAll };
}
