using System.Net;

namespace SqlBackup.Website.Tests;

public sealed class GoogleIntegrationTests
{
    [Fact]
    public async Task Without_settings_no_google_code_is_rendered()
    {
        using var factory = new WebsiteFactory();
        using var response = await factory.CreateClient().GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("googletagmanager", html);
        Assert.DoesNotContain("google-site-verification", html);
        Assert.DoesNotContain("data-consent-banner", html);
        Assert.DoesNotContain("googletagmanager", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Analytics_loads_gtag_with_consent_denied_by_default()
    {
        using var factory = new WebsiteFactory(new Dictionary<string, string?> { ["GoogleAnalytics:MeasurementId"] = "G-ABC123XYZ9" });
        using var response = await factory.CreateClient().GetAsync("/features");
        var html = await response.Content.ReadAsStringAsync();
        var doc = Html.Parse(html);

        Assert.NotNull(doc.QuerySelector("script[src='https://www.googletagmanager.com/gtag/js?id=G-ABC123XYZ9']"));
        Assert.Contains("gtag('config', 'G-ABC123XYZ9')", html);
        Assert.Contains("analytics_storage: 'denied'", html);
        Assert.NotNull(doc.QuerySelector("[data-consent-banner]"));

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("https://*.googletagmanager.com", csp);
        Assert.Contains("https://*.google-analytics.com", csp);
    }

    [Fact]
    public async Task Analytics_without_consent_requirement_grants_storage_and_hides_the_banner()
    {
        using var factory = new WebsiteFactory(new Dictionary<string, string?>
        {
            ["GoogleAnalytics:MeasurementId"] = "G-ABC123XYZ9",
            ["GoogleAnalytics:RequireConsent"] = "false",
        });
        var html = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("analytics_storage: 'granted'", html);
        Assert.DoesNotContain("data-consent-banner", html);
    }

    [Fact]
    public void An_invalid_measurement_id_stops_the_site_from_starting()
    {
        using var factory = new WebsiteFactory(new Dictionary<string, string?> { ["GoogleAnalytics:MeasurementId"] = "G-'; alert(1); //" });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("MeasurementId", error.ToString());
    }

    [Theory]
    [InlineData("ContactEmail:Smtp:Security", "SSL")]
    [InlineData("ContactEmail:Smtp:Security", "9")]
    [InlineData("Site:TrustedProxies:0", "not-an-ip")]
    [InlineData("Site:RepositoryUrl", "")]
    public void Invalid_settings_stop_the_site_with_a_clear_message(string key, string value)
    {
        using var factory = new WebsiteFactory(new Dictionary<string, string?> { [key] = value });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(key.Split(':')[^1].TrimEnd('0').TrimEnd(':'), error.ToString());
    }

    [Fact]
    public async Task Optional_owner_url_may_be_empty()
    {
        using var factory = new WebsiteFactory(new Dictionary<string, string?> { ["Site:OwnerUrl"] = "" });
        using var response = await factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Search_console_meta_tag_and_html_file_are_served_when_configured()
    {
        using var factory = new WebsiteFactory(new Dictionary<string, string?>
        {
            ["GoogleSearchConsole:VerificationToken"] = "AbC-123_token",
            ["GoogleSearchConsole:HtmlVerificationFile"] = "google0123456789abcdef.html",
        });
        var client = factory.CreateClient();

        var doc = await client.GetDocumentAsync("/");
        Assert.Equal("AbC-123_token", doc.Meta("google-site-verification"));

        Assert.Equal("google-site-verification: google0123456789abcdef.html",
            await client.GetStringAsync("/google0123456789abcdef.html"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/googleffffffffffffffff.html")).StatusCode);
    }

    [Fact]
    public async Task Verification_file_route_is_404_when_not_configured()
    {
        using var factory = new WebsiteFactory();
        using var response = await factory.CreateClient().GetAsync("/google0123456789abcdef.html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
