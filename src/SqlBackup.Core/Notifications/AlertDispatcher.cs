using Microsoft.Extensions.Logging;
using SqlBackup.Core.Models;
using SqlBackup.Core.Monitoring;
using SqlBackup.Core.Security;

namespace SqlBackup.Core.Notifications;

/// <summary>
/// Fans one event out to every enabled channel (email, webhook, Windows Event Log).
/// Channel failures are logged and swallowed — alerting must never fail a backup.
/// </summary>
public sealed class AlertDispatcher
{
    private readonly ILogger _log;
    private readonly ISecretProtector _protector;

    public AlertDispatcher(ILogger log, ISecretProtector protector)
    {
        _log = log;
        _protector = protector;
    }

    public async Task SendJobReportAsync(
        NotificationSettings settings,
        string jobName,
        IReadOnlyList<JobHistoryEntry> entries,
        CancellationToken ct = default)
    {
        if (entries.Count == 0)
            return;
        var anyFailure = entries.Any(e => !e.IsFullySuccessful);

        try
        {
            await EmailNotifier.SendJobReportAsync(settings, _protector, jobName, entries, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Email notification failed");
        }

        if (settings.WebhookEnabled && settings.WebhookUrl.Length > 0 && (anyFailure || !settings.OnlyOnFailure))
        {
            try
            {
                await WebhookNotifier.SendAsync(settings.WebhookUrl, WebhookNotifier.BuildJobPayload(jobName, entries), ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Webhook notification failed");
            }
        }

        if (settings.EventLogEnabled)
        {
            try
            {
                EventLogNotifier.WriteJobReport(jobName, entries);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Event log write failed");
            }
        }
    }

    /// <summary>RPO breaches always alert on every enabled channel (they are failures by definition).</summary>
    public async Task SendRpoBreachesAsync(
        NotificationSettings settings,
        IReadOnlyList<RpoBreach> breaches,
        CancellationToken ct = default)
    {
        if (breaches.Count == 0)
            return;
        var headline = $"SqlBackup RPO alert on {Environment.MachineName}: {breaches.Count} target(s) have no recent successful backup";
        var details = breaches.Select(b => b.Describe()).ToList();

        if (EmailNotifier.ShouldSend(settings, anyFailure: true))
        {
            try
            {
                await EmailNotifier.SendCustomAsync(settings, _protector,
                    $"{settings.SubjectPrefix} RPO alert: {breaches.Count} overdue backup target(s)",
                    string.Join(Environment.NewLine, details), ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "RPO email failed");
            }
        }

        if (settings.WebhookEnabled && settings.WebhookUrl.Length > 0)
        {
            try
            {
                await WebhookNotifier.SendAsync(settings.WebhookUrl,
                    WebhookNotifier.BuildAlertPayload("rpo-breach", headline, details), ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "RPO webhook failed");
            }
        }

        if (settings.EventLogEnabled)
        {
            try
            {
                EventLogNotifier.WriteAlert(headline, details, EventLogNotifier.EventIdRpoBreach);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "RPO event log write failed");
            }
        }
    }
}
