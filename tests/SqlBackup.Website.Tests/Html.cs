using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace SqlBackup.Website.Tests;

internal static class Html
{
    private static readonly HtmlParser Parser = new();

    public static async Task<IHtmlDocument> GetDocumentAsync(this HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode, $"GET {url} returned {(int)response.StatusCode}");
        return Parser.ParseDocument(await response.Content.ReadAsStringAsync());
    }

    public static IHtmlDocument Parse(string html) => Parser.ParseDocument(html);

    public static string? Meta(this IHtmlDocument doc, string nameOrProperty) =>
        doc.QuerySelector($"meta[name='{nameOrProperty}'], meta[property='{nameOrProperty}']")?.GetAttribute("content");
}
