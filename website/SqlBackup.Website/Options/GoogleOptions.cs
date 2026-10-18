namespace SqlBackup.Website.Options;

/// <summary>Google Analytics 4, bound from the "GoogleAnalytics" section. Empty MeasurementId = analytics off.</summary>
public sealed class GoogleAnalyticsOptions
{
    public const string SectionName = "GoogleAnalytics";

    /// <summary>GA4 measurement ID, e.g. "G-ABC123XYZ9" (Admin → Data streams → Web).</summary>
    public string MeasurementId { get; set; } = "";

    /// <summary>
    /// Ask visitors before analytics cookies are set (Google Consent Mode v2: analytics
    /// storage starts as "denied" and is granted only after the visitor accepts).
    /// </summary>
    public bool RequireConsent { get; set; } = true;

    public bool Enabled => !string.IsNullOrWhiteSpace(MeasurementId);
}

/// <summary>Google Search Console ownership verification, bound from the "GoogleSearchConsole" section.</summary>
public sealed class SearchConsoleOptions
{
    public const string SectionName = "GoogleSearchConsole";

    /// <summary>
    /// The content value of the "HTML tag" method:
    /// &lt;meta name="google-site-verification" content="THIS_VALUE" /&gt;.
    /// </summary>
    public string VerificationToken { get; set; } = "";

    /// <summary>
    /// File name of the "HTML file" method, e.g. "google1234567890abcdef.html". The site
    /// serves it from the root with the content Google expects.
    /// </summary>
    public string HtmlVerificationFile { get; set; } = "";
}
