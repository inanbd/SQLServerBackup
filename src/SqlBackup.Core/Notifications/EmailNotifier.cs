using System.Net;
using System.Net.Mail;
using System.Text;
using SqlBackup.Core.Models;
using SqlBackup.Core.Security;

namespace SqlBackup.Core.Notifications;

/// <summary>Sends job result emails over SMTP. Failures here must never fail a backup run.</summary>
public static class EmailNotifier
{
    public static bool ShouldSend(NotificationSettings settings, bool anyFailure) =>
        settings.EmailEnabled &&
        !string.IsNullOrWhiteSpace(settings.SmtpHost) &&
        !string.IsNullOrWhiteSpace(settings.FromAddress) &&
        !string.IsNullOrWhiteSpace(settings.ToAddresses) &&
        (anyFailure || !settings.OnlyOnFailure);

    public static async Task SendJobReportAsync(
        NotificationSettings settings,
        ISecretProtector protector,
        string jobName,
        IReadOnlyList<JobHistoryEntry> entries,
        CancellationToken ct = default)
    {
        var failures = entries.Count(e => !e.IsFullySuccessful);
        if (!ShouldSend(settings, failures > 0))
            return;

        var subject = $"{settings.SubjectPrefix} {jobName}: " +
                      (failures > 0 ? $"FAILED ({failures} of {entries.Count} databases)" : $"OK ({entries.Count} databases)");

        var body = new StringBuilder();
        body.AppendLine($"Backup job: {jobName}");
        body.AppendLine($"Machine:    {Environment.MachineName}");
        body.AppendLine($"Finished:   {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        body.AppendLine();
        foreach (var e in entries)
        {
            var state = !e.Success ? "FAILED" : e.OffsiteSuccess == false ? "OK, OFF-SITE FAILED" : "OK";
            body.AppendLine($"[{state}] {e.Database} ({e.Type}), {e.DurationSeconds:F1}s");
            if (e.FilePath is not null)
                body.AppendLine($"    File: {e.FilePath}" + (e.FileSizeBytes is { } size ? $" ({size / (1024.0 * 1024.0):F1} MB)" : ""));
            if (!string.IsNullOrEmpty(e.Message))
                body.AppendLine($"    Note: {e.Message}");
            if (!string.IsNullOrEmpty(e.Error))
                body.AppendLine($"    Error: {e.Error}");
        }

        await SendAsync(settings, protector, subject, body.ToString(), ct);
    }

    public static async Task SendTestAsync(NotificationSettings settings, ISecretProtector protector, CancellationToken ct = default)
    {
        await SendAsync(settings, protector,
            $"{settings.SubjectPrefix} Test email",
            $"This is a test email from the SQL Server Backup suite on {Environment.MachineName}.", ct);
    }

    /// <summary>Free-form alert (used for RPO breaches and similar non-run notifications).</summary>
    public static Task SendCustomAsync(
        NotificationSettings settings, ISecretProtector protector, string subject, string body, CancellationToken ct = default) =>
        SendAsync(settings, protector, subject, body, ct);

    private static async Task SendAsync(
        NotificationSettings settings,
        ISecretProtector protector,
        string subject,
        string body,
        CancellationToken ct)
    {
        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.UseTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
        {
            var password = settings.ProtectedSmtpPassword is { Length: > 0 } blob ? protector.Unprotect(blob) : "";
            client.Credentials = new NetworkCredential(settings.SmtpUsername, password);
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress),
            Subject = subject,
            Body = body,
        };
        foreach (var to in settings.ToAddresses.Split(';', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            message.To.Add(to);

        await client.SendMailAsync(message, ct).WaitAsync(TimeSpan.FromSeconds(60), ct);
    }
}
