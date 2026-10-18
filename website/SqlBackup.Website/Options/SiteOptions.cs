using System.ComponentModel.DataAnnotations;

namespace SqlBackup.Website.Options;

/// <summary>General site identity and links, bound from the "Site" section of appsettings.json.</summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    [Required]
    public string Name { get; set; } = "SQL Server Backup Suite";

    /// <summary>Public origin of the site (scheme + host, no trailing slash). Used for canonical URLs, the sitemap and Open Graph tags.</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "";

    /// <summary>Copyright holder shown in the footer and in structured data.</summary>
    [Required]
    public string Owner { get; set; } = "";

    /// <summary>Profile page of the owner (structured data "author"/"publisher" URL).</summary>
    [Url]
    public string OwnerUrl { get; set; } = "";

    /// <summary>Product version shown on the site and in structured data.</summary>
    public string Version { get; set; } = "1.0.0";

    [Url]
    public string RepositoryUrl { get; set; } = "";

    /// <summary>Where every "Download" button points (GitHub Releases by default).</summary>
    [Url]
    public string DownloadUrl { get; set; } = "";

    [Url]
    public string IssuesUrl { get; set; } = "";

    /// <summary>Optional X/Twitter account (with or without "@") for twitter:site.</summary>
    public string TwitterSite { get; set; } = "";

    /// <summary>False on staging copies: robots.txt disallows everything and pages send noindex.</summary>
    public bool AllowIndexing { get; set; } = true;

    /// <summary>Honor X-Forwarded-For/Proto from a reverse proxy (nginx, a load balancer, a container platform).</summary>
    public bool BehindReverseProxy { get; set; }

    /// <summary>Absolute URL for a site-relative path, e.g. "/features" -> "https://example.com/features".</summary>
    public string AbsoluteUrl(string path) =>
        BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
}
