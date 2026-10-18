using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SqlBackup.Website.Pages;

[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    public int Code { get; private set; }

    public string? RequestId { get; private set; }

    public bool IsNotFound => Code == StatusCodes.Status404NotFound;

    public void OnGet(int? code) => Handle(code);

    // Re-executed POSTs (e.g. a 400 from an invalid form) land here too.
    public void OnPost(int? code) => Handle(code);

    private void Handle(int? code)
    {
        Code = code ?? StatusCodes.Status500InternalServerError;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        if (code is null)
            Response.StatusCode = StatusCodes.Status500InternalServerError;
    }
}
