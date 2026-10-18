using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SqlBackup.Website.Infrastructure;

public sealed record Screenshot(string Name, int Width, int Height, string WindowTitle, string Alt)
{
    public string Src => $"/img/screens/{Name}.webp";
    public string Src2x => $"/img/screens/{Name}@2x.webp";
}

/// <summary>
/// The application screenshots in wwwroot/img/screens. Each exists as {name}.webp (1x)
/// and {name}@2x.webp; sizes are the 1x pixel size so the browser reserves space (no layout shift).
/// </summary>
public static class Screenshots
{
    private const string MainWindow = "SQL Server Backup — Control Panel";

    private static readonly Dictionary<string, Screenshot> All = new[]
    {
        new Screenshot("dashboard", 1145, 709, MainWindow,
            "SQL Server Backup dashboard: backup service running, scheduled jobs with next run, last run and last result, Run now buttons"),
        new Screenshot("dashboard-running", 1145, 709, MainWindow,
            "Dashboard while the nightly full SQL Server backup job is running"),
        new Screenshot("connections", 1145, 709, MainWindow,
            "SQL Server connections list with SQL login and Windows authentication profiles"),
        new Screenshot("connection-editor", 517, 528, "SQL Server connection",
            "SQL Server connection editor after a successful Test connection to SQL Server 2022"),
        new Screenshot("dedicated-backup-login", 517, 541, "SQL Server connection",
            "Dedicated backup login created with a generated password and db_backupoperator rights on every database"),
        new Screenshot("backup-jobs", 1145, 709, MainWindow,
            "Backup jobs list: full, differential and transaction log jobs with schedules, destinations, storage mode and retention"),
        new Screenshot("job-editor", 675, 729, "Backup job",
            "Backup job editor: all user databases with exclusions, daily schedule and schedule preview"),
        new Screenshot("job-editor-options", 675, 729, "Backup job",
            "Backup job editor: destination folder, retention, off-site copy, RPO alert and backup options like RESTORE VERIFYONLY and compression"),
        new Screenshot("offsite-destinations", 1145, 709, MainWindow,
            "Off-site destinations: Amazon S3, Azure Blob Storage, SFTP, Google Drive and a Windows file share"),
        new Screenshot("offsite-smb-share", 577, 395, "Off-site destination",
            "Windows file share (SMB) off-site destination after a successful connection test"),
        new Screenshot("offsite-google-drive", 577, 550, "Off-site destination",
            "Google Drive off-site destination authorized with a Google account"),
        new Screenshot("offsite-amazon-s3", 577, 416, "Off-site destination",
            "Amazon S3 off-site destination with bucket, region and access key"),
        new Screenshot("history", 1145, 709, MainWindow,
            "Backup history with file, size, duration and details of a verified, off-site-copied full backup"),
        new Screenshot("reports", 1145, 709, MainWindow,
            "Backup reports: 30-day success rate, run counts, backup footprint and per-database size trends"),
        new Screenshot("settings-service", 1145, 709, MainWindow,
            "Settings: install, start and stop the SQL Server backup Windows service; email notification settings"),
        new Screenshot("settings-notifications", 1145, 709, MainWindow,
            "Settings: email (SMTP), Slack/Teams webhook and Windows Event Log alerts, configuration export and import"),
    }.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<Screenshot> Catalog => All.Values;

    public static Screenshot Get(string name) =>
        All.TryGetValue(name, out var shot) ? shot : throw new ArgumentException($"Unknown screenshot '{name}'.", nameof(name));
}

/// <summary>
/// &lt;screenshot name="dashboard" caption="..." priority /&gt; renders a framed, responsive,
/// click-to-enlarge application screenshot.
/// </summary>
[HtmlTargetElement("screenshot", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ScreenshotTagHelper : TagHelper
{
    public string Name { get; set; } = "";

    public string? Caption { get; set; }

    /// <summary>Overrides the catalog alt text.</summary>
    public string? Alt { get; set; }

    /// <summary>Above-the-fold image: load eagerly with high fetch priority (helps Largest Contentful Paint).</summary>
    public bool Priority { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var shot = Screenshots.Get(Name);
        var dialog = shot.Width < 900;
        var alt = Alt ?? shot.Alt;
        var loading = Priority ? "loading=\"eager\" fetchpriority=\"high\"" : "loading=\"lazy\"";

        output.TagName = "figure";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", dialog ? "shot shot--dialog" : "shot");

        var html = $"""
            <div class="window">
              <div class="window__bar" aria-hidden="true"><span class="window__title">{Encode(shot.WindowTitle)}</span><span class="window__controls"><i></i><i></i><i></i></span></div>
              <a class="window__body" href="{shot.Src2x}" data-lightbox title="Open full-size screenshot">
                <img src="{shot.Src}" srcset="{shot.Src} 1x, {shot.Src2x} 2x" width="{shot.Width}" height="{shot.Height}" alt="{Encode(alt)}" {loading} decoding="async">
              </a>
            </div>
            """;
        if (!string.IsNullOrWhiteSpace(Caption))
            html += $"<figcaption>{Encode(Caption)}</figcaption>";
        output.Content.SetHtmlContent(html);
    }

    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
}
