using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SqlBackup.Website.Infrastructure;

namespace SqlBackup.Website.Tests;

public sealed class SiteBehaviorTests : IClassFixture<WebsiteFactory>
{
    private readonly WebsiteFactory _factory;

    public SiteBehaviorTests(WebsiteFactory factory) => _factory = factory;

    [Fact]
    public async Task Responses_carry_security_headers_and_inline_scripts_carry_the_csp_nonce()
    {
        using var response = await _factory.CreateClient().GetAsync("/");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        var nonce = Regex.Match(csp, "'nonce-([^']+)'").Groups[1].Value;

        Assert.NotEmpty(nonce);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());

        var doc = Html.Parse(await response.Content.ReadAsStringAsync());
        Assert.All(doc.QuerySelectorAll("script:not([src])"), s => Assert.Equal(nonce, s.GetAttribute("nonce")));
        Assert.Empty(doc.QuerySelectorAll("[style]"));
    }

    [Fact]
    public async Task Content_pages_set_no_cookies()
    {
        var client = _factory.CreateClient();
        foreach (var path in new[] { "/", "/features", "/docs", "/faq" })
        {
            using var response = await client.GetAsync(path);
            Assert.False(response.Headers.Contains("Set-Cookie"), $"{path} sets a cookie");
        }
    }

    [Fact]
    public async Task Download_page_shows_the_build_version_by_default()
    {
        var doc = await _factory.CreateClient().GetDocumentAsync("/download");

        Assert.Equal("v1.0.0", doc.QuerySelector(".version")?.TextContent);
        Assert.Contains("SqlBackupSetup-1.0.0.exe", doc.Body!.TextContent);
    }

    [Fact]
    public async Task License_page_renders_the_repository_license()
    {
        var doc = await _factory.CreateClient().GetDocumentAsync("/license");
        var text = doc.QuerySelector("pre.license-text")?.TextContent ?? "";

        Assert.StartsWith("MIT License", text);
        Assert.Contains("Copyright (c) 2026 inanbd", text);
    }

    [Fact]
    public async Task Every_internal_link_resolves()
    {
        var client = _factory.CreateClient();
        var checkedLinks = new HashSet<string>();
        foreach (var page in Seo.SitePages.Indexed)
        {
            var doc = await client.GetDocumentAsync(page.Path);
            foreach (var href in doc.QuerySelectorAll("a[href^='/']").Select(a => a.GetAttribute("href")!))
            {
                var target = href.Split('#')[0];
                if (target.Length == 0 || !checkedLinks.Add(target))
                    continue;
                using var response = await client.GetAsync(target);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{page.Path} links to {href} which returns {(int)response.StatusCode}");
            }
        }
    }

    [Fact]
    public async Task Fragment_links_point_at_existing_anchors()
    {
        var client = _factory.CreateClient();
        foreach (var page in Seo.SitePages.Indexed)
        {
            var doc = await client.GetDocumentAsync(page.Path);
            foreach (var href in doc.QuerySelectorAll("a[href*='#']").Select(a => a.GetAttribute("href")!).Where(h => h.StartsWith('/') || h.StartsWith('#')))
            {
                var parts = href.Split('#', 2);
                var target = parts[0].Length == 0 ? doc : await client.GetDocumentAsync(parts[0]);
                Assert.True(target.GetElementById(parts[1]) is not null, $"{page.Path}: link {href} points to a missing anchor");
            }
        }
    }

    [Fact]
    public void Every_screenshot_exists_in_both_resolutions_with_the_declared_size()
    {
        var webRoot = _factory.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        foreach (var shot in Screenshots.Catalog)
        {
            var oneX = Path.Combine(webRoot, shot.Src.TrimStart('/'));
            var twoX = Path.Combine(webRoot, shot.Src2x.TrimStart('/'));
            Assert.True(File.Exists(oneX), $"{oneX} is missing");
            Assert.True(File.Exists(twoX), $"{twoX} is missing");
            Assert.Equal((shot.Width, shot.Height), WebP.ReadSize(oneX));
            Assert.Equal((shot.Width * 2, shot.Height * 2), WebP.ReadSize(twoX));
        }
    }

    [Fact]
    public async Task Static_assets_are_compressed_and_cached()
    {
        var client = _factory.CreateClient();
        var doc = await client.GetDocumentAsync("/");
        var css = doc.QuerySelector("link[rel=stylesheet]")!.GetAttribute("href")!;
        Assert.Contains("?v=", css);

        using var request = new HttpRequestMessage(HttpMethod.Get, css);
        request.Headers.AcceptEncoding.ParseAdd("br");
        using var response = await client.SendAsync(request);

        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
        Assert.Contains("br", response.Content.Headers.ContentEncoding);
    }
}

/// <summary>Reads the canvas size from a WebP file header (VP8, VP8L and VP8X).</summary>
internal static class WebP
{
    public static (int Width, int Height) ReadSize(string path)
    {
        var b = File.ReadAllBytes(path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(b, 8, 4));
        var chunk = System.Text.Encoding.ASCII.GetString(b, 12, 4);
        return chunk switch
        {
            "VP8 " => (BitConverter.ToUInt16(b, 26) & 0x3FFF, BitConverter.ToUInt16(b, 28) & 0x3FFF),
            "VP8L" => (1 + (((b[22] & 0x3F) << 8) | b[21]), 1 + (((b[24] & 0x0F) << 10) | (b[23] << 2) | ((b[22] & 0xC0) >> 6))),
            "VP8X" => (1 + (b[24] | (b[25] << 8) | (b[26] << 16)), 1 + (b[27] | (b[28] << 8) | (b[29] << 16))),
            _ => throw new InvalidDataException($"Unknown WebP chunk '{chunk}' in {path}"),
        };
    }
}
