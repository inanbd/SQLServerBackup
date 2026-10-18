namespace SqlBackup.Website.Seo;

public sealed record SitePage(string Path, double Priority, string ChangeFrequency);

/// <summary>
/// Every public page and its sitemap hints. A test fails when a routable page is
/// neither listed here nor deliberately kept out of search (<see cref="NotIndexed"/>).
/// </summary>
public static class SitePages
{
    public static readonly IReadOnlyList<SitePage> Indexed =
    [
        new("/", 1.0, "weekly"),
        new("/features", 0.9, "monthly"),
        new("/sql-server-express-backup", 0.9, "monthly"),
        new("/download", 0.8, "monthly"),
        new("/docs", 0.8, "monthly"),
        new("/docs/backup-jobs", 0.8, "monthly"),
        new("/docs/off-site-backups", 0.8, "monthly"),
        new("/docs/monitoring-and-alerts", 0.7, "monthly"),
        new("/docs/troubleshooting", 0.7, "monthly"),
        new("/faq", 0.7, "monthly"),
        new("/contact", 0.5, "yearly"),
        new("/license", 0.3, "yearly"),
        new("/privacy", 0.3, "yearly"),
    ];

    /// <summary>Pages that exist but carry noindex and stay out of the sitemap.</summary>
    public static readonly IReadOnlySet<string> NotIndexed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/contact/thank-you",
        "/error",
    };
}
