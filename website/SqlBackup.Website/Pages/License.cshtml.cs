using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SqlBackup.Website.Pages;

public sealed class LicenseModel : PageModel
{
    // The repository's LICENSE file is copied next to the binaries at build time.
    private static readonly Lazy<string> Text = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
        return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : "";
    });

    public string LicenseText => Text.Value;
}
