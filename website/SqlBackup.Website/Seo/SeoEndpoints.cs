using System.Text;
using System.Xml;
using Microsoft.Extensions.Options;
using SqlBackup.Website.Options;

namespace SqlBackup.Website.Seo;

/// <summary>sitemap.xml, robots.txt and the optional Google Search Console verification file.</summary>
public static class SeoEndpoints
{
    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sitemap.xml", (IOptions<SiteOptions> site) =>
            Results.Text(BuildSitemap(site.Value, SiteBuildDate), "application/xml", Encoding.UTF8));

        app.MapGet("/robots.txt", (IOptions<SiteOptions> site) =>
            Results.Text(BuildRobots(site.Value), "text/plain", Encoding.UTF8));

        // Search Console "HTML file" verification: /google0123456789abcdef.html
        app.MapGet("/{file}.html", (string file, IOptions<SearchConsoleOptions> gsc) =>
        {
            var expected = gsc.Value.HtmlVerificationFile.Trim();
            return expected.Length > 0 && string.Equals($"{file}.html", expected, StringComparison.OrdinalIgnoreCase)
                ? Results.Text($"google-site-verification: {expected}", "text/html", Encoding.UTF8)
                : Results.NotFound();
        });

        return app;
    }

    /// <summary>Content ships with the build, so the build time is the honest lastmod.</summary>
    internal static readonly DateTime SiteBuildDate =
        File.GetLastWriteTimeUtc(typeof(SeoEndpoints).Assembly.Location);

    internal static string BuildSitemap(SiteOptions site, DateTime lastModifiedUtc)
    {
        const string ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var builder = new StringBuilder();
        using (var xml = XmlWriter.Create(builder, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", ns);
            foreach (var page in SitePages.Indexed)
            {
                xml.WriteStartElement("url", ns);
                xml.WriteElementString("loc", ns, site.AbsoluteUrl(page.Path));
                xml.WriteElementString("lastmod", ns, lastModifiedUtc.ToString("yyyy-MM-dd"));
                xml.WriteElementString("changefreq", ns, page.ChangeFrequency);
                xml.WriteElementString("priority", ns, page.Priority.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }
        // XmlWriter over a StringBuilder always declares utf-16; the response is UTF-8.
        return builder.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
    }

    internal static string BuildRobots(SiteOptions site)
    {
        var robots = new StringBuilder("User-agent: *\n");
        if (!site.AllowIndexing)
            return robots.Append("Disallow: /\n").ToString();

        robots.Append("Allow: /\n");
        foreach (var path in SitePages.NotIndexed)
            robots.Append("Disallow: ").Append(path).Append('\n');
        robots.Append('\n').Append("Sitemap: ").Append(site.AbsoluteUrl("/sitemap.xml")).Append('\n');
        return robots.ToString();
    }
}
