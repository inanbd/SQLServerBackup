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

    public List<string> Databases { get; set; } = new();

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
}
