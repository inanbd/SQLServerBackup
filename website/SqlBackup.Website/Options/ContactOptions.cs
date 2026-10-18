namespace SqlBackup.Website.Options;

/// <summary>Where contact-form messages go and how they are sent, bound from the "ContactEmail" section.</summary>
public sealed class ContactEmailOptions
{
    public const string SectionName = "ContactEmail";

    /// <summary>Recipient(s) of contact-form messages; separate several with ';' or ','.</summary>
    public string ToAddress { get; set; } = "";

    public string ToName { get; set; } = "";

    /// <summary>Sender address. Most SMTP providers require an address the SMTP account may send as.</summary>
    public string FromAddress { get; set; } = "";

    public string FromName { get; set; } = "SQL Server Backup Suite website";

    public string SubjectPrefix { get; set; } = "[Website contact]";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>
    /// When set, messages are written as .eml files to this folder instead of being sent —
    /// handy for local development without an SMTP server. Relative paths are resolved
    /// against the content root.
    /// </summary>
    public string PickupDirectory { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ToAddress) && !string.IsNullOrWhiteSpace(FromAddress) &&
        (!string.IsNullOrWhiteSpace(Smtp.Host) || !string.IsNullOrWhiteSpace(PickupDirectory));
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    /// <summary>None, Auto, SslOnConnect (usually port 465), StartTls (usually port 587) or StartTlsWhenAvailable.</summary>
    public string Security { get; set; } = "StartTls";

    /// <summary>Leave empty for servers that accept unauthenticated relay (e.g. a local relay).</summary>
    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Abuse protection for the contact form, bound from the "ContactForm" section.</summary>
public sealed class ContactFormOptions
{
    public const string SectionName = "ContactForm";

    /// <summary>Messages accepted per client IP address within <see cref="WindowMinutes"/>.</summary>
    public int PermitLimit { get; set; } = 5;

    public int WindowMinutes { get; set; } = 15;

    /// <summary>Submissions faster than this after the form was rendered are treated as bots.</summary>
    public int MinimumSecondsToSubmit { get; set; } = 3;

    /// <summary>A rendered form stays valid this long; older submissions are asked to resend.</summary>
    public int MaximumFormAgeHours { get; set; } = 12;
}
