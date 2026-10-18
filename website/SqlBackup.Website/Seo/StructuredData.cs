using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Html;
using SqlBackup.Website.Options;

namespace SqlBackup.Website.Seo;

/// <summary>Builds schema.org JSON-LD blocks.</summary>
public static class StructuredData
{
    private static readonly JsonSerializerOptions Json = new()
    {
        // The default encoder escapes '<' and '>', so "</script>" can never close the block early.
        Encoder = JavaScriptEncoder.Default,
        WriteIndented = false,
    };

    public static IHtmlContent Script(object data, string nonce) =>
        new HtmlString($"<script type=\"application/ld+json\" nonce=\"{nonce}\">{JsonSerializer.Serialize(data, Json)}</script>");

    public static object WebSite(SiteOptions site) => new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "WebSite",
        ["name"] = site.Name,
        ["url"] = site.AbsoluteUrl("/"),
        ["publisher"] = Owner(site),
    };

    public static object SoftwareApplication(SiteOptions site, IEnumerable<string> screenshotPaths) => new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "SoftwareApplication",
        ["name"] = site.Name,
        ["description"] =
            "Free, open-source Windows service and control panel that schedules native SQL Server backups " +
            "(full, differential and transaction log) for every edition including SQL Server Express, " +
            "verifies them, enforces retention and copies them off-site to SMB shares, Amazon S3, Azure Blob Storage, SFTP or Google Drive.",
        ["applicationCategory"] = "BusinessApplication",
        ["applicationSubCategory"] = "Database backup software",
        ["operatingSystem"] = "Windows 10, Windows 11, Windows Server 2016 or later",
        ["softwareVersion"] = site.ProductVersion,
        ["license"] = site.AbsoluteUrl("/license"),
        ["isAccessibleForFree"] = true,
        ["url"] = site.AbsoluteUrl("/"),
        ["downloadUrl"] = site.DownloadUrl,
        ["codeRepository"] = site.RepositoryUrl,
        ["screenshot"] = screenshotPaths.Select(site.AbsoluteUrl).ToArray(),
        ["offers"] = new Dictionary<string, object>
        {
            ["@type"] = "Offer",
            ["price"] = "0",
            ["priceCurrency"] = "USD",
        },
        ["author"] = Owner(site),
    };

    public static object Breadcrumbs(SiteOptions site, IReadOnlyList<Breadcrumb> crumbs) => new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "BreadcrumbList",
        ["itemListElement"] = crumbs.Select((crumb, i) => new Dictionary<string, object>
        {
            ["@type"] = "ListItem",
            ["position"] = i + 1,
            ["name"] = crumb.Name,
            ["item"] = site.AbsoluteUrl(crumb.Path),
        }).ToArray(),
    };

    public static object TechArticle(SiteOptions site, string headline, string description, string path) => new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "TechArticle",
        ["headline"] = headline,
        ["description"] = description,
        ["url"] = site.AbsoluteUrl(path),
        ["mainEntityOfPage"] = site.AbsoluteUrl(path),
        ["dateModified"] = SeoEndpoints.SiteBuildDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["author"] = Owner(site),
        ["publisher"] = Owner(site),
        ["about"] = new Dictionary<string, object> { ["@type"] = "SoftwareApplication", ["name"] = site.Name },
    };

    public static object FaqPage(IEnumerable<(string Question, string Answer)> items) => new Dictionary<string, object>
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "FAQPage",
        ["mainEntity"] = items.Select(item => new Dictionary<string, object>
        {
            ["@type"] = "Question",
            ["name"] = item.Question,
            ["acceptedAnswer"] = new Dictionary<string, object> { ["@type"] = "Answer", ["text"] = item.Answer },
        }).ToArray(),
    };

    private static object Owner(SiteOptions site) => new Dictionary<string, object>
    {
        ["@type"] = "Person",
        ["name"] = site.Owner,
        ["url"] = string.IsNullOrEmpty(site.OwnerUrl) ? site.AbsoluteUrl("/") : site.OwnerUrl,
    };
}
