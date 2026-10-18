using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SqlBackup.Website.Pages;

public sealed record FaqItem(string Question, string AnswerHtml)
{
    /// <summary>Plain-text answer for FAQPage structured data.</summary>
    public string AnswerText =>
        WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(AnswerHtml, "<[^>]+>", " "), @"\s+", " ")).Trim();
}

public sealed record FaqSection(string Title, IReadOnlyList<FaqItem> Items);

public sealed class FaqModel : PageModel
{
    public static readonly IReadOnlyList<FaqSection> Sections =
    [
        new("General",
        [
            new("What is SQL Server Backup Suite?",
                "<p>A free, open-source Windows application that automates Microsoft SQL Server backups. A Windows service schedules and runs native full, differential and transaction log backups, verifies them, applies retention, copies them off-site and sends alerts. A desktop control panel configures and monitors everything.</p>"),
            new("Is it really free? Can I use it commercially?",
                "<p>Yes. It is released under the <a href=\"/license\">MIT License</a>: use it on any number of servers, at home or in a company, modify it and redistribute it. There are no paid editions or feature limits.</p>"),
            new("Which SQL Server versions and editions are supported?",
                "<p>All editions: Express, Web, Standard, Enterprise and Developer. Backups use SQL Server's own <code>BACKUP DATABASE</code> and <code>BACKUP LOG</code> commands, so every SQL Server version Microsoft still supports works. Native backup compression depends on the edition and is not available on Express.</p>"),
            new("Does it work with SQL Server Express?",
                "<p>Yes &mdash; that is one of the main reasons it exists. SQL Server Express has no SQL Server Agent, so it cannot schedule backups by itself. The SQL Server Backup service provides the scheduling. See <a href=\"/sql-server-express-backup\">how to schedule SQL Server Express backups</a>.</p>"),
            new("Do I need SQL Server Agent?",
                "<p>No. Scheduling, retries, retention and alerts are handled by the SQL Server Backup Windows service. SQL Server Agent is neither required nor used.</p>"),
            new("Which Windows versions does it run on?",
                "<p>Windows 10, Windows 11 and Windows Server 2016 or later, 64-bit. The installer is self-contained, so no separate .NET runtime is needed.</p>"),
        ]),
        new("Backups and restores",
        [
            new("Which backup types are supported?",
                "<p>Full, differential and transaction log backups, plus copy-only full and log backups. Combine them in separate jobs &mdash; for example a nightly full backup, differentials during the day and log backups every 15 minutes &mdash; for point-in-time recovery.</p>"),
            new("Can it back up a remote SQL Server or several instances?",
                "<p>Yes. Add one connection per instance, local or remote, and create jobs for each. For a remote server, write backups to a network share that both the SQL Server service account and the backup service can reach, because SQL Server writes the file on its own machine.</p>"),
            new("Does it back up system databases?",
                "<p>Yes, if you want it to. The <em>AllDatabases</em> selection includes <code>master</code>, <code>model</code> and <code>msdb</code>; <em>AllUserDatabases</em> leaves them out. Explicit lists can contain any database.</p>"),
            new("How do I restore a backup?",
                "<p>The files are standard SQL Server backups, so restore them with SQL Server Management Studio (<em>Databases &rarr; Restore Database</em>) or T-SQL, for example <code>RESTORE DATABASE [SalesDB] FROM DISK = N'D:\\SqlBackups\\SalesDB\\SalesDB_Full_20261018_013000.bak' WITH REPLACE</code>. For point-in-time recovery, restore the full backup <code>WITH NORECOVERY</code>, then the newest differential and the following log backups.</p>"),
            new("Are the backups verified?",
                "<p>Yes, when <em>Verify backup after completion</em> is on (the default), every file is checked with <code>RESTORE VERIFYONLY</code> right after it is written, and <code>WITH CHECKSUM</code> validates the pages. A failed verification is reported like any other failure. Test restores remain good practice.</p>"),
            new("How does retention decide what to delete?",
                "<p>Per job you keep everything, the last N backups per database and type, or backups younger than N days. Retention is chain-aware: a full backup is never deleted while newer differential or log backups depend on it. Only files with this tool's naming pattern are touched and the newest backup is always kept.</p>"),
            new("What happens if the server is off at the scheduled time?",
                "<p>With <em>catch up once at startup</em> enabled, a missed occurrence runs once when the service starts again. Independently, an RPO alert tells you when a job has had no successful backup for longer than you allow.</p>"),
        ]),
        new("Off-site copies, alerts and security",
        [
            new("Can I back up SQL Server to Amazon S3, Azure or Google Drive?",
                "<p>Yes. Each finished backup can be copied to a Windows file share, Amazon S3 or an S3-compatible service (MinIO, Backblaze B2, Wasabi), Azure Blob Storage, an SFTP server or Google Drive, with retries and a separate off-site retention policy. See <a href=\"/docs/off-site-backups\">off-site backups</a>.</p>"),
            new("How will I know when a backup fails?",
                "<p>Through email, a Slack/Microsoft Teams/Discord-compatible webhook and the Windows Event Log, plus the dashboard and tray icon. RPO alerts also fire when no backup happened at all, for example because a job was disabled.</p>"),
            new("Where are passwords stored? Is it secure?",
                "<p>All passwords, keys and tokens are encrypted with Windows DPAPI in <code>C:\\ProgramData\\SqlBackup\\config.json</code> and are never exported. For least privilege, let the app create a dedicated backup login that holds only backup rights.</p>"),
            new("Does the application send any data to the internet?",
                "<p>No telemetry. The application only connects to the SQL Servers, off-site destinations and alert channels you configure.</p>"),
            new("How do I move my configuration to another server?",
                "<p>Use <em>Settings &rarr; Export&hellip;</em> to save connections, jobs, destinations and settings as JSON, then <em>Import&hellip;</em> on the new machine. Secrets are never exported; the import lists which credentials to enter again.</p>"),
            new("How do I get help or report a bug?",
                "<p>Check <a href=\"/docs/troubleshooting\">Troubleshooting</a>, open an issue on GitHub, or use the <a href=\"/contact\">contact form</a>.</p>"),
        ]),
    ];

    public static IEnumerable<FaqItem> AllItems => Sections.SelectMany(s => s.Items);
}
