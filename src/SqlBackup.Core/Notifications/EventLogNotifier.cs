using System.Diagnostics;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Notifications;

/// <summary>
/// Windows Event Log channel (source "SqlBackup" in the Application log).
/// The source is auto-created on first write when the process has the rights
/// (the service running as LocalSystem does); otherwise the write throws and
/// the dispatcher logs it — event log problems never fail a backup.
/// </summary>
public static class EventLogNotifier
{
    public const string Source = "SqlBackup";

    public const int EventIdSuccess = 1000;
    public const int EventIdFailure = 1001;
    public const int EventIdRpoBreach = 1002;

    public static void WriteJobReport(string jobName, IReadOnlyList<JobHistoryEntry> entries)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var failures = entries.Count(e => !e.IsFullySuccessful);
        var lines = entries.Select(e =>
            $"[{(!e.Success ? "FAILED" : e.OffsiteSuccess == false ? "OK, OFF-SITE FAILED" : "OK")}] " +
            $"{e.Database} ({e.Type}) {e.DurationSeconds:F1}s" +
            (e.Error is null ? "" : $" — {e.Error}"));
        var message = $"Backup job '{jobName}': {(failures > 0 ? $"{failures} of {entries.Count} databases failed" : $"OK ({entries.Count} databases)")}"
                      + Environment.NewLine + string.Join(Environment.NewLine, lines);

        EventLog.WriteEntry(Source, Truncate(message),
            failures > 0 ? EventLogEntryType.Error : EventLogEntryType.Information,
            failures > 0 ? EventIdFailure : EventIdSuccess);
    }

    public static void WriteAlert(string headline, IReadOnlyList<string> details, int eventId)
    {
        if (!OperatingSystem.IsWindows())
            return;
        EventLog.WriteEntry(Source, Truncate(headline + Environment.NewLine + string.Join(Environment.NewLine, details)),
            EventLogEntryType.Warning, eventId);
    }

    // Event log entries are capped at ~31k characters.
    private static string Truncate(string message) =>
        message.Length <= 30_000 ? message : message[..30_000] + " …";
}
