using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SqlBackup.Website.Infrastructure;
using SqlBackup.Website.Options;
using SqlBackup.Website.Services;

namespace SqlBackup.Website.Pages;

public sealed class ContactInput
{
    [Required(ErrorMessage = "Please enter your name.")]
    [StringLength(100, ErrorMessage = "Please keep your name under 100 characters.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Please enter your email address so we can reply.")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(254)]
    public string Email { get; set; } = "";

    [StringLength(150, ErrorMessage = "Please keep the company name under 150 characters.")]
    public string? Company { get; set; }

    [Required(ErrorMessage = "Please choose a topic.")]
    public string Topic { get; set; } = "";

    [Required(ErrorMessage = "Please enter a message.")]
    [StringLength(5000, MinimumLength = 20, ErrorMessage = "Please write between 20 and 5,000 characters.")]
    public string Message { get; set; } = "";
}

[EnableRateLimiting(ContactRateLimit.PolicyName)]
public sealed class ContactModel : PageModel
{
    public static readonly IReadOnlyList<string> Topics =
    [
        "General question",
        "Help with installation or setup",
        "Bug report",
        "Feature request",
        "Business or licensing",
        "Other",
    ];

    private readonly IContactMailer _mailer;
    private readonly IDataProtector _tokens;
    private readonly ContactFormOptions _form;
    private readonly TimeProvider _clock;
    private readonly ILogger<ContactModel> _log;

    public ContactModel(
        IContactMailer mailer,
        IDataProtectionProvider dataProtection,
        IOptions<ContactFormOptions> form,
        TimeProvider clock,
        ILogger<ContactModel> log)
    {
        _mailer = mailer;
        _tokens = dataProtection.CreateProtector("SqlBackup.Website.ContactForm.v1");
        _form = form.Value;
        _clock = clock;
        _log = log;
    }

    [BindProperty]
    public ContactInput Input { get; set; } = new();

    /// <summary>Honeypot: hidden from people, filled in by naive bots.</summary>
    [BindProperty]
    public string? Website { get; set; }

    /// <summary>Encrypted render time; rejects instant (bot) and stale submissions.</summary>
    [BindProperty]
    public string? FormToken { get; set; }

    public string? ErrorMessage { get; private set; }

    public bool RateLimited { get; private set; }

    public void OnGet(bool limited = false)
    {
        RateLimited = limited;
        IssueToken();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var issued = ReadToken();

        // Bots get the same "thank you" page as people, so they learn nothing from the response.
        if (!string.IsNullOrEmpty(Website) ||
            (issued is { } renderedAt && now - renderedAt < TimeSpan.FromSeconds(_form.MinimumSecondsToSubmit)))
        {
            _log.LogInformation("Contact form submission discarded as automated (honeypot or too fast).");
            return RedirectToPage("/ContactThankYou");
        }

        if (issued is null || now - issued.Value > TimeSpan.FromHours(Math.Max(1, _form.MaximumFormAgeHours)))
            ModelState.AddModelError(string.Empty, "This form has expired. Please check your message and send it again.");

        if (!string.IsNullOrEmpty(Input.Topic) && !Topics.Contains(Input.Topic))
            ModelState.AddModelError("Input.Topic", "Please choose a topic from the list.");

        if (!ModelState.IsValid)
            return PageWithFreshToken();

        if (!_mailer.IsConfigured)
        {
            _log.LogError("Contact form submitted but the ContactEmail settings are incomplete.");
            ErrorMessage = "Sorry, the contact form is not available right now. Please try again later or open an issue on GitHub.";
            return PageWithFreshToken();
        }

        var message = new ContactMessage(
            Input.Name.Trim(),
            Input.Email.Trim(),
            string.IsNullOrWhiteSpace(Input.Company) ? null : Input.Company.Trim(),
            Input.Topic,
            Input.Message.Trim(),
            now,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString());

        try
        {
            await _mailer.SendAsync(message, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Sending the contact form message failed.");
            ErrorMessage = "Sorry, your message could not be sent. Please try again in a few minutes or open an issue on GitHub.";
            return PageWithFreshToken();
        }

        return RedirectToPage("/ContactThankYou");
    }

    private PageResult PageWithFreshToken()
    {
        IssueToken();
        return Page();
    }

    private void IssueToken()
    {
        FormToken = _tokens.Protect(_clock.GetUtcNow().UtcTicks.ToString(CultureInfo.InvariantCulture));
        ModelState.Remove(nameof(FormToken));
    }

    private DateTimeOffset? ReadToken()
    {
        if (string.IsNullOrEmpty(FormToken))
            return null;
        try
        {
            return long.TryParse(_tokens.Unprotect(FormToken), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                ? new DateTimeOffset(ticks, TimeSpan.Zero)
                : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
