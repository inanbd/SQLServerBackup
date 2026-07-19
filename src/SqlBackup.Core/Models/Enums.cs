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
