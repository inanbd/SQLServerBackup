using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SqlBackup.Website.Infrastructure;
using SqlBackup.Website.Options;
using SqlBackup.Website.Seo;
using SqlBackup.Website.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<SiteOptions>()
    .Bind(builder.Configuration.GetSection(SiteOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => o.OwnerUrl.Length == 0 || Uri.TryCreate(o.OwnerUrl, UriKind.Absolute, out _),
        "Site:OwnerUrl must be an absolute URL or empty.")
    .Validate(o => o.TrustedProxies.All(p => IPAddress.TryParse(p, out _) || System.Net.IPNetwork.TryParse(p, out _)),
        "Site:TrustedProxies must contain IP addresses or CIDR networks such as 10.0.0.0/8.")
    .ValidateOnStart();
builder.Services.AddOptions<GoogleAnalyticsOptions>()
    .Bind(builder.Configuration.GetSection(GoogleAnalyticsOptions.SectionName))
    .Validate(o => !o.Enabled || Regex.IsMatch(o.MeasurementId, "^[A-Z]{1,3}-[A-Z0-9]{4,20}$"),
        "GoogleAnalytics:MeasurementId must look like G-XXXXXXXXXX (or be empty to disable analytics).")
    .ValidateOnStart();
builder.Services.AddOptions<SearchConsoleOptions>()
    .Bind(builder.Configuration.GetSection(SearchConsoleOptions.SectionName))
    .Validate(o => o.HtmlVerificationFile.Length == 0 || Regex.IsMatch(o.HtmlVerificationFile, @"^google[0-9a-zA-Z]+\.html$"),
        "GoogleSearchConsole:HtmlVerificationFile must be the file name Google gives you, e.g. google0123456789abcdef.html.")
    .ValidateOnStart();
builder.Services.AddOptions<ContactEmailOptions>()
    .Bind(builder.Configuration.GetSection(ContactEmailOptions.SectionName))
    .Validate(o => SmtpContactMailer.TryParseSecurity(o.Smtp.Security, out _),
        "ContactEmail:Smtp:Security must be one of None, Auto, SslOnConnect, StartTls or StartTlsWhenAvailable.")
    .ValidateOnStart();
builder.Services.AddOptions<ContactFormOptions>().Bind(builder.Configuration.GetSection(ContactFormOptions.SectionName));

builder.Services.AddRazorPages(o =>
{
    // Index.cshtml also answers on "/Index"; keep "/" as the home page's only URL.
    o.Conventions.AddPageRouteModelConvention("/Index", model =>
    {
        foreach (var selector in model.Selectors.Where(s => s.AttributeRouteModel?.Template == "Index").ToList())
            model.Selectors.Remove(selector);
    });
});
builder.Services.Configure<RouteOptions>(o => o.LowercaseUrls = true);
builder.Services.AddScoped<CspNonce>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IContactMailer, SmtpContactMailer>();
builder.Services.AddSingleton<ContactSendLimiter>();

// Antiforgery and contact-form tokens are encrypted with these keys. Persist them (for example
// on a container volume) so open forms keep working across restarts and redeploys.
var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(keysDirectory))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("SqlBackup.Website")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.IsPathRooted(keysDirectory)
            ? keysDirectory
            : Path.Combine(builder.Environment.ContentRootPath, keysDirectory)));
}

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["image/svg+xml", "application/manifest+json"]);
});

builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = false;
});

if (builder.Configuration.GetValue<bool>($"{SiteOptions.SectionName}:{nameof(SiteOptions.BehindReverseProxy)}"))
{
    var trusted = builder.Configuration.GetSection($"{SiteOptions.SectionName}:{nameof(SiteOptions.TrustedProxies)}").Get<string[]>() ?? [];
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
        // Without a list every sender is trusted, which is only safe when the app port is
        // reachable from the proxy alone (see website/README.md).
        foreach (var proxy in trusted)
        {
            if (IPAddress.TryParse(proxy, out var address))
                o.KnownProxies.Add(address);
            else if (System.Net.IPNetwork.TryParse(proxy, out var network))
                o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(network.BaseAddress, network.PrefixLength));
        }
    });
}

var app = builder.Build();

if (app.Services.GetRequiredService<IOptions<SiteOptions>>().Value.BehindReverseProxy)
    app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCanonicalUrls();
app.UseResponseCompression();
app.UseSecurityHeaders();
app.UseStatusCodePagesWithReExecute("/error/{0}");

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // asp-append-version adds ?v=<hash>, so those URLs can be cached forever.
        var versioned = ctx.Context.Request.Query.ContainsKey("v");
        ctx.Context.Response.Headers[HeaderNames.CacheControl] = versioned
            ? "public, max-age=31536000, immutable"
            : "public, max-age=604800";
    },
});

app.UseRouting();

app.MapRazorPages();
app.MapSeoEndpoints();

var mailOptions = app.Services.GetRequiredService<IOptions<ContactEmailOptions>>().Value;
if (!mailOptions.IsConfigured)
    app.Logger.LogWarning("ContactEmail settings are incomplete: the contact form will show an error instead of sending mail.");

app.Run();

/// <summary>Entry point; partial so integration tests can reference it with WebApplicationFactory.</summary>
public partial class Program
{
}
