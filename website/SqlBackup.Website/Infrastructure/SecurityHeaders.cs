using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SqlBackup.Website.Options;

namespace SqlBackup.Website.Infrastructure;

/// <summary>Per-request nonce that inline &lt;script&gt; blocks must carry to satisfy the Content-Security-Policy.</summary>
public sealed class CspNonce
{
    public string Value { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
}

public static class SecurityHeaders
{
    /// <summary>
    /// Adds CSP (nonce-based; Google Analytics hosts only when analytics is on) and the
    /// usual hardening headers to every response.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var nonce = context.RequestServices.GetRequiredService<CspNonce>().Value;
            var analytics = context.RequestServices.GetRequiredService<IOptions<GoogleAnalyticsOptions>>().Value.Enabled;
            var allowIndexing = context.RequestServices.GetRequiredService<IOptions<SiteOptions>>().Value.AllowIndexing;

            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = BuildCsp(nonce, analytics);
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                if (!allowIndexing)
                    headers["X-Robots-Tag"] = "noindex, nofollow";
                return Task.CompletedTask;
            });

            await next();
        });

    internal static string BuildCsp(string nonce, bool analytics)
    {
        // Hosts from Google's documented CSP guidance for Google Analytics 4 / gtag.js. Once
        // consent is granted, GA4 also sends hits to www.google.com and doubleclick (Google signals).
        var gaScript = analytics ? " https://*.googletagmanager.com" : "";
        var gaImg = analytics
            ? " https://*.google-analytics.com https://*.googletagmanager.com https://*.g.doubleclick.net https://*.google.com"
            : "";
        var gaConnect = analytics
            ? " https://*.google-analytics.com https://*.analytics.google.com https://*.googletagmanager.com https://*.g.doubleclick.net https://*.google.com"
            : "";

        return string.Join("; ",
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}'{gaScript}",
            "style-src 'self'",
            $"img-src 'self' data:{gaImg}",
            $"connect-src 'self'{gaConnect}",
            "font-src 'self'",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            "frame-ancestors 'none'");
    }
}
