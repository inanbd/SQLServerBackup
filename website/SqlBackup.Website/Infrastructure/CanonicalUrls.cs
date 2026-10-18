namespace SqlBackup.Website.Infrastructure;

public static class CanonicalUrls
{
    /// <summary>
    /// 301-redirects page URLs with uppercase letters or a trailing slash ("/Features/",
    /// "/docs/") to their canonical lowercase form, so search engines see one URL per page.
    /// Static files (anything with an extension) are left alone.
    /// </summary>
    public static IApplicationBuilder UseCanonicalUrls(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var path = request.Path.Value;
            if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) &&
                path is { Length: > 1 } && !Path.HasExtension(path))
            {
                var canonical = path.TrimEnd('/').ToLowerInvariant();
                if (canonical.Length == 0)
                    canonical = "/";
                if (!string.Equals(canonical, path, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                    context.Response.Headers.Location = request.PathBase + canonical + request.QueryString;
                    return;
                }
            }
            await next();
        });
}
