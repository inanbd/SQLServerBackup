using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
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
builder.Services.AddOptions<ContactEmailOptions>().Bind(builder.Configuration.GetSection(ContactEmailOptions.SectionName));
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

// Contact form: a fixed number of POSTs per client IP per window; reading the page is never limited.
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy(ContactRateLimit.PolicyName, context =>
    {
        if (!HttpMethods.IsPost(context.Request.Method))
            return RateLimitPartition.GetNoLimiter("read");

        var form = context.RequestServices.GetRequiredService<IOptions<ContactFormOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, form.PermitLimit),
                Window = TimeSpan.FromMinutes(Math.Max(1, form.WindowMinutes)),
                QueueLimit = 0,
            });
    });
    o.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        context.HttpContext.Response.Headers.Location = "/contact?limited=true";
        return ValueTask.CompletedTask;
    };
});

if (builder.Configuration.GetValue<bool>($"{SiteOptions.SectionName}:{nameof(SiteOptions.BehindReverseProxy)}"))
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // The proxy address is not known in advance on most hosting platforms.
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
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
app.UseRateLimiter();

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
