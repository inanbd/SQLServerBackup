namespace SqlBackup.Core.Models;

public sealed class NotificationSettings
{
    public bool EmailEnabled { get; set; }

    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;

    /// <summary>Use STARTTLS/SSL when talking to the SMTP server.</summary>
    public bool UseTls { get; set; } = true;

    public string? SmtpUsername { get; set; }

    /// <summary>DPAPI-protected SMTP password blob.</summary>
    public string? ProtectedSmtpPassword { get; set; }

    public string FromAddress { get; set; } = "";

    /// <summary>Recipient addresses, separated by ';' or ','.</summary>
    public string ToAddresses { get; set; } = "";

    public bool OnlyOnFailure { get; set; } = true;

    public string SubjectPrefix { get; set; } = "[SqlBackup]";

    /// <summary>POST job results as JSON to a webhook (payload includes Slack/Discord-compatible text fields).</summary>
    public bool WebhookEnabled { get; set; }

    public string WebhookUrl { get; set; } = "";

    /// <summary>Write job results to the Windows Event Log (source "SqlBackup").</summary>
    public bool EventLogEnabled { get; set; }
}

public sealed class ServiceSettings
{
    /// <summary>How often the scheduler loop checks for due jobs.</summary>
    public int SchedulerPollSeconds { get; set; } = 5;

    /// <summary>Extra connection attempts after the first one fails (with exponential backoff).</summary>
    public int SqlConnectRetries { get; set; } = 3;

    /// <summary>Base delay for the retry backoff; attempt i waits base * 3^i seconds.</summary>
    public int SqlRetryBaseDelaySeconds { get; set; } = 5;

    /// <summary>Timeout for the BACKUP command itself. 0 = no limit.</summary>
    public int CommandTimeoutSeconds { get; set; }

    /// <summary>Trace, Debug, Information, Warning, Error.</summary>
    public string MinimumLogLevel { get; set; } = "Information";

    public int LogRetentionDays { get; set; } = 31;

    /// <summary>Warn in the job history when destination free space drops below this.</summary>
    public int MinFreeDiskSpaceWarnMb { get; set; } = 512;

    /// <summary>How often the service evaluates RPO (missing-backup) alerts.</summary>
    public int RpoCheckMinutes { get; set; } = 15;
}

/// <summary>
/// The whole shared configuration document, persisted as JSON in
/// <see cref="AppPaths.ConfigFile"/>. Written by the desktop app, read by the service.
/// </summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    public DateTimeOffset ModifiedUtc { get; set; }

    public List<ConnectionProfile> Connections { get; set; } = new();

    public List<BackupJob> Jobs { get; set; } = new();

    public List<OffsiteDestination> OffsiteDestinations { get; set; } = new();

    public NotificationSettings Notifications { get; set; } = new();

    public ServiceSettings Service { get; set; } = new();

    public ConnectionProfile? FindConnection(Guid id) => Connections.FirstOrDefault(c => c.Id == id);

    public BackupJob? FindJob(Guid id) => Jobs.FirstOrDefault(j => j.Id == id);

    public OffsiteDestination? FindOffsiteDestination(Guid? id) =>
        id is { } value ? OffsiteDestinations.FirstOrDefault(d => d.Id == value) : null;
}
