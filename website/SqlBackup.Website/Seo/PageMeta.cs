using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace SqlBackup.Website.Seo;

public sealed record Breadcrumb(string Name, string Path);

/// <summary>
/// Per-page SEO data (title, description, social image, breadcrumbs, extra JSON-LD).
/// Pages fill it in with <c>ViewData.Meta()</c>; the layout renders it into &lt;head&gt;.
/// </summary>
public sealed class PageMeta
{
    /// <summary>Page title; the site name is appended unless <see cref="AppendSiteName"/> is false.</summary>
    public string Title { get; set; } = "";

    public bool AppendSiteName { get; set; } = true;

    /// <summary>Meta description — aim for 120–155 characters; it is the snippet under the search result.</summary>
    public string Description { get; set; } = "";

    /// <summary>Open Graph / Twitter image (site-relative path).</summary>
    public string Image { get; set; } = "/img/og-image.png";

    public string ImageAlt { get; set; } = "SQL Server Backup Suite dashboard showing scheduled SQL Server backup jobs";

    public int ImageWidth { get; set; } = 1200;

    public int ImageHeight { get; set; } = 630;

    /// <summary>"website" for landing pages, "article" for documentation.</summary>
    public string OgType { get; set; } = "website";

    public bool NoIndex { get; set; }

    public List<Breadcrumb> Breadcrumbs { get; } = new();

    /// <summary>Additional schema.org objects rendered as JSON-LD.</summary>
    public List<object> StructuredData { get; } = new();
}

public static class PageMetaExtensions
{
    private const string Key = "__PageMeta";

    public static PageMeta Meta(this ViewDataDictionary viewData)
    {
        if (viewData[Key] is not PageMeta meta)
        {
            meta = new PageMeta();
            viewData[Key] = meta;
        }
        return meta;
    }
}
