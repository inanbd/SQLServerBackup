using System.Globalization;
using System.Net;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using SqlBackup.Website.Options;

namespace SqlBackup.Website.Services;

/// <summary>A validated contact-form submission plus request context for the recipient.</summary>
public sealed record ContactMessage(
    string Name,
    string Email,
    string? Company,
    string Topic,
    string Message,
    DateTimeOffset ReceivedUtc,
    string? ClientIp,
    string? UserAgent);

public interface IContactMailer
{
    bool IsConfigured { get; }

    Task SendAsync(ContactMessage message, CancellationToken ct = default);
}

/// <summary>Sends contact-form messages over SMTP (MailKit), or drops them as .eml files into a pickup folder.</summary>
public sealed class SmtpContactMailer : IContactMailer
{
    private readonly IOptionsMonitor<ContactEmailOptions> _options;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SmtpContactMailer> _log;

    public SmtpContactMailer(IOptionsMonitor<ContactEmailOptions> options, IWebHostEnvironment env, ILogger<SmtpContactMailer> log)
    {
        _options = options;
        _env = env;
        _log = log;
    }

    public bool IsConfigured => _options.CurrentValue.IsConfigured;

    public async Task SendAsync(ContactMessage message, CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        if (!options.IsConfigured)
            throw new InvalidOperationException("The contact form email settings (ContactEmail section) are incomplete.");

        var mime = BuildMessage(message, options);

        if (!string.IsNullOrWhiteSpace(options.PickupDirectory))
        {
            var folder = Path.IsPathRooted(options.PickupDirectory)
                ? options.PickupDirectory
                : Path.Combine(_env.ContentRootPath, options.PickupDirectory);
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder,
                $"{message.ReceivedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.eml");
            await mime.WriteToAsync(file, ct);
            _log.LogInformation("Contact message from {Email} written to {File}", message.Email, file);
            return;
        }

        using var client = new SmtpClient { Timeout = Math.Max(5, options.Smtp.TimeoutSeconds) * 1000 };
        await client.ConnectAsync(options.Smtp.Host, options.Smtp.Port, ParseSecurity(options.Smtp.Security), ct);
        if (!string.IsNullOrEmpty(options.Smtp.Username))
            await client.AuthenticateAsync(options.Smtp.Username, options.Smtp.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
        _log.LogInformation("Contact message from {Email} sent via {Host}", message.Email, options.Smtp.Host);
    }

    internal static MimeMessage BuildMessage(ContactMessage message, ContactEmailOptions options)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        foreach (var address in SplitAddresses(options.ToAddress))
            mime.To.Add(new MailboxAddress(options.ToName, address));
        // Replying goes straight to the visitor; the From address stays one the SMTP account may use.
        mime.ReplyTo.Add(new MailboxAddress(message.Name, message.Email));
        mime.Subject = SingleLine($"{options.SubjectPrefix} {message.Topic}: {message.Name}".Trim());

        var fields = new (string Label, string? Value)[]
        {
            ("Name", message.Name),
            ("Email", message.Email),
            ("Company", message.Company),
            ("Topic", message.Topic),
            ("Received (UTC)", message.ReceivedUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            ("IP address", message.ClientIp),
            ("User agent", message.UserAgent),
        };

        var text = new StringBuilder();
        foreach (var (label, value) in fields)
            text.Append(label).Append(": ").AppendLine(string.IsNullOrWhiteSpace(value) ? "-" : value);
        text.AppendLine().AppendLine(message.Message);

        var html = new StringBuilder("<table cellpadding=\"4\" style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px\">");
        foreach (var (label, value) in fields)
            html.Append("<tr><th align=\"left\" valign=\"top\">").Append(WebUtility.HtmlEncode(label))
                .Append("</th><td>").Append(WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "-" : value))
                .Append("</td></tr>");
        html.Append("</table><hr><div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;white-space:pre-wrap\">")
            .Append(WebUtility.HtmlEncode(message.Message)).Append("</div>");

        mime.Body = new BodyBuilder { TextBody = text.ToString(), HtmlBody = html.ToString() }.ToMessageBody();
        return mime;
    }

    /// <summary>
    /// True when MimeKit accepts the value as a bare address. [EmailAddress] only checks for a
    /// single '@', so values like "Ada &lt;ada@example.com&gt;" would otherwise fail at send time.
    /// </summary>
    public static bool IsValidAddress(string value)
    {
        try
        {
            return new MailboxAddress(string.Empty, value).Address == value;
        }
        catch (ParseException)
        {
            return false;
        }
    }

    internal static IEnumerable<string> SplitAddresses(string value) =>
        value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Parses Smtp:Security by name only (startup validation rejects anything else).</summary>
    internal static bool TryParseSecurity(string value, out SecureSocketOptions security) =>
        Enum.TryParse(value, ignoreCase: true, out security) &&
        Enum.IsDefined(security) &&
        !value.Trim().All(char.IsAsciiDigit);

    internal static SecureSocketOptions ParseSecurity(string value) =>
        TryParseSecurity(value, out var security)
            ? security
            : throw new InvalidOperationException($"ContactEmail:Smtp:Security '{value}' is not one of: {string.Join(", ", Enum.GetNames<SecureSocketOptions>())}.");

    private static string SingleLine(string value) =>
        string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}
