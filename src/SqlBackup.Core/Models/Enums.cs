namespace SqlBackup.Core.Models;

public enum SqlAuthMode
{
    Windows,
    Sql,
}

public enum BackupType
{
    Full,
    Differential,
    TransactionLog,
}

public enum ScheduleKind
{
    /// <summary>Run every N minutes, anchored to the previous run.</summary>
    Interval,
    /// <summary>Run once per day at a fixed time.</summary>
    Daily,
    /// <summary>Run on selected weekdays at a fixed time.</summary>
    Weekly,
    /// <summary>Run according to a cron expression (5-field, optional seconds).</summary>
    Cron,
}

public enum RetentionMode
{
    KeepAll,
    KeepLastN,
    MaxAgeDays,
}

public enum DatabaseSelectionMode
{
    /// <summary>Back up exactly the databases listed on the job.</summary>
    Explicit,
    /// <summary>Back up every online user database, discovered at run time, minus exclusions.</summary>
    AllUserDatabases,
    /// <summary>Like AllUserDatabases but including master/model/msdb.</summary>
    AllDatabases,
}

public enum OffsiteKind
{
    AzureBlob,
    S3,
    Sftp,
    /// <summary>Windows file share (UNC path), optionally with its own credentials.</summary>
    SmbShare,
    GoogleDrive,
}

public enum GoogleDriveAuthMode
{
    /// <summary>
    /// A Google Cloud service account key. Fully unattended, but service accounts have
    /// no Drive storage of their own — the target folder must live in a Shared Drive
    /// (Google Workspace) the service account is a member of.
    /// </summary>
    ServiceAccount,

    /// <summary>
    /// A user account authorized once interactively in the desktop app; the service
    /// then uses the stored refresh token. Works with ordinary (My Drive) accounts.
    /// </summary>
    OAuthUser,
}

public enum BackupStorageMode
{
    /// <summary>Keep the backup in the job's destination folder and also copy it off-site.</summary>
    LocalAndOffsite,

    /// <summary>Keep the backup only in the job's destination folder (no off-site copy).</summary>
    LocalOnly,

    /// <summary>
    /// Treat the destination folder as staging: after a successful off-site upload the
    /// local file is deleted. A failed upload always keeps the local file.
    /// </summary>
    OffsiteOnly,
}

public enum RunTrigger
{
    Scheduled,
    /// <summary>Fired late because the scheduled occurrence was missed (service was down).</summary>
    CatchUp,
    /// <summary>Requested through the service IPC endpoint ("Run now").</summary>
    Manual,
    /// <summary>Executed directly inside the desktop app, bypassing the service.</summary>
    ManualStandalone,
}
