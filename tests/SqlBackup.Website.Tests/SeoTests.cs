using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SqlBackup.Website.Seo;

namespace SqlBackup.Website.Tests;

public sealed class SeoTests : IClassFixture<WebsiteFactory>
{
    private readonly WebsiteFactory _factory;

    public SeoTests(WebsiteFactory factory) => _factory = factory;

    public static TheoryData<string> IndexedPages()
    {
        var data = new TheoryData<string>();
        foreach (var page in SitePages.Indexed)
            data.Add(page.Path);
        return data;
    }

    [Theory]
    [MemberData(nameof(IndexedPages))]
    public async Task Every_indexed_page_has_complete_search_metadata(string path)
    {
        var doc = await _factory.CreateClient().GetDocumentAsync(path);

        Assert.Single(doc.QuerySelectorAll("h1"));
        Assert.InRange(doc.Title!.Length, 10, 70);
        Assert.InRange(doc.Meta("description")!.Length, 50, 160);
        Assert.Equal(WebsiteFactory.BaseUrl + path, doc.QuerySelector("link[rel=canonical]")?.GetAttribute("href"));
        Assert.StartsWith("index", doc.Meta("robots"));
        Assert.Equal(WebsiteFactory.BaseUrl + path, doc.Meta("og:url"));
        Assert.StartsWith(WebsiteFactory.BaseUrl + "/", doc.Meta("og:image"));
        Assert.Equal("summary_large_image", doc.Meta("twitter:card"));
        Assert.Equal("en", doc.DocumentElement.GetAttribute("lang"));

        foreach (var img in doc.QuerySelectorAll("main img"))
        {
            Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("alt")), $"{path}: <img src={img.GetAttribute("src")}> has no alt text");
            Assert.NotNull(img.GetAttribute("width"));
            Assert.NotNull(img.GetAttribute("height"));
        }

        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
            JsonDocument.Parse(script.TextContent).Dispose();
    }

    [Fact]
    public async Task Titles_and_descriptions_are_unique()
    {
        var client = _factory.CreateClient();
        var titles = new Dictionary<string, string>();
        var descriptions = new Dictionary<string, string>();
        foreach (var page in SitePages.Indexed)
        {
            var doc = await client.GetDocumentAsync(page.Path);
            Assert.True(titles.TryAdd(doc.Title!, page.Path), $"Duplicate title on {page.Path} and {titles.GetValueOrDefault(doc.Title!)}");
            var description = doc.Meta("description")!;
            Assert.True(descriptions.TryAdd(description, page.Path), $"Duplicate description on {page.Path}");
        }
    }

    [Fact]
    public void Every_routable_page_is_in_the_sitemap_or_explicitly_not_indexed()
    {
        var routes = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<PageActionDescriptor>() is not null)
            .Select(e => "/" + string.Join('/', e.RoutePattern.PathSegments
                .Select(s => string.Concat(s.Parts.OfType<Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart>().Select(p => p.Content)))
                .Where(s => s.Length > 0)))
            .Distinct()
            .ToList();

        Assert.NotEmpty(routes);
        var known = SitePages.Indexed.Select(p => p.Path).Concat(SitePages.NotIndexed).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes)
            Assert.True(known.Contains(route), $"Page route '{route}' is neither in SitePages.Indexed nor SitePages.NotIndexed.");
    }

    [Fact]
    public async Task Sitemap_lists_every_indexed_page_with_absolute_urls()
    {
        var response = await _factory.CreateClient().GetAsync("/sitemap.xml");
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync());
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = xml.Descendants(ns + "loc").Select(e => e.Value).ToList();

        Assert.Equal(SitePages.Indexed.Select(p => WebsiteFactory.BaseUrl + p.Path), locations);
        Assert.All(xml.Descendants(ns + "lastmod"), e => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", e.Value));
    }

    [Fact]
    public void Sitemap_dates_are_gregorian_whatever_the_server_culture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
            var xml = SeoEndpoints.BuildSitemap(new Options.SiteOptions { BaseUrl = WebsiteFactory.BaseUrl }, new DateTime(2026, 10, 18));
            Assert.Contains("<lastmod>2026-10-18</lastmod>", xml);
            Assert.Contains("<priority>1.0</priority>", xml);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Robots_txt_points_to_the_sitemap_and_hides_utility_pages()
    {
        var robots = await _factory.CreateClient().GetStringAsync("/robots.txt");

        Assert.Contains("Allow: /", robots);
        Assert.Contains("Disallow: /contact/thank-you", robots);
        Assert.Contains($"Sitemap: {WebsiteFactory.BaseUrl}/sitemap.xml", robots);
    }

    [Fact]
    public async Task Staging_sites_block_indexing_everywhere()
    {
        using var staging = new WebsiteFactory(new Dictionary<string, string?> { ["Site:AllowIndexing"] = "false" });
        var client = staging.CreateClient();

        var robots = await client.GetStringAsync("/robots.txt");
        Assert.Contains("Disallow: /", robots);
        Assert.DoesNotContain("Sitemap:", robots);

        using var response = await client.GetAsync("/features");
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        var doc = Html.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("noindex", doc.Meta("robots"));
        Assert.Null(doc.QuerySelector("link[rel=canonical]"));
    }

    [Fact]
    public async Task Home_page_describes_the_software_in_structured_data()
    {
        var doc = await _factory.CreateClient().GetDocumentAsync("/");
        var types = doc.QuerySelectorAll("script[type='application/ld+json']")
            .Select(s => JsonDocument.Parse(s.TextContent).RootElement.GetProperty("@type").GetString())
            .ToList();

        Assert.Contains("WebSite", types);
        Assert.Contains("SoftwareApplication", types);
    }

    [Fact]
    public async Task Faq_structured_data_matches_the_visible_questions()
    {
        var doc = await _factory.CreateClient().GetDocumentAsync("/faq");
        var faq = doc.QuerySelectorAll("script[type='application/ld+json']")
            .Select(s => JsonDocument.Parse(s.TextContent).RootElement)
            .Single(e => e.GetProperty("@type").GetString() == "FAQPage");

        var questions = faq.GetProperty("mainEntity").EnumerateArray().Select(q => q.GetProperty("name").GetString() ?? "").ToList();
        var visible = doc.QuerySelectorAll(".faq summary").Select(s => s.TextContent.Trim()).ToList();
        Assert.Equal(visible, questions);
        Assert.All(faq.GetProperty("mainEntity").EnumerateArray(),
            q => Assert.DoesNotContain("<", q.GetProperty("acceptedAnswer").GetProperty("text").GetString()));
    }

    [Theory]
    [InlineData("/Features", "/features")]
    [InlineData("/docs/", "/docs")]
    [InlineData("/Docs/Backup-Jobs/?ref=x", "/docs/backup-jobs?ref=x")]
    [InlineData("//evil.example/", "/evil.example")]
    [InlineData("/\\evil.example/", "/evil.example")]
    [InlineData("/%C3%9Cber/", "/%C3%BCber")]
    [InlineData("/Foo%3Fx/", "/foo%3Fx")]
    public async Task Non_canonical_urls_redirect_permanently(string requested, string expected)
    {
        // An absolute URI, so "//host/" is sent as a path rather than read as another host.
        using var response = await _factory.CreateNoRedirectClient().GetAsync(new Uri("http://localhost" + requested));

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Unknown_pages_return_404_with_the_custom_page()
    {
        using var response = await _factory.CreateClient().GetAsync("/no-such-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var doc = Html.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Page not found", doc.QuerySelector("h1")?.TextContent);
        Assert.StartsWith("noindex", doc.Meta("robots"));
    }
}
