using System.Net.Http;
using System.Text;
using System.Text.Json;
using SqlBackup.Core.Config;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Notifications;

/// <summary>
/// Generic JSON webhook channel. The payload carries structured fields plus
/// "text" (Slack/Teams-style) and "content" (Discord-style) duplicates so plain
/// incoming-webhook endpoints render something readable out of the box.
/// </summary>
public static class WebhookNotifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed class WebhookPayload
    {
        public string Event { get; set; } = "";
        public string Text { get; set; } = "";
        public string Content { get; set; } = "";
        public string Machine { get; set; } = Environment.MachineName;
        public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
        public string? Job { get; set; }
        public bool? Success { get; set; }
        public List<WebhookEntry> Entries { get; set; } = new();
        public List<string> Details { get; set; } = new();
    }

    public sealed class WebhookEntry
    {
        public string Database { get; set; } = "";
        public string Type { get; set; } = "";
        public bool Success { get; set; }
        public bool? OffsiteSuccess { get; set; }
        public string? File { get; set; }
        public long? SizeBytes { get; set; }
        public double DurationSeconds { get; set; }
        public string? Error { get; set; }
    }

    public static WebhookPayload BuildJobPayload(string jobName, IReadOnlyList<JobHistoryEntry> entries)
    {
        var failures = entries.Count(e => !e.IsFullySuccessful);
        var text = failures > 0
            ? $"❌ SqlBackup job '{jobName}' on {Environment.MachineName}: FAILED ({failures} of {entries.Count} databases)"
            : $"✅ SqlBackup job '{jobName}' on {Environment.MachineName}: OK ({entries.Count} databases)";

        var payload = new WebhookPayload
        {
            Event = "backup-result",
            Text = text,
            Content = text,
            Job = jobName,
            Success = failures == 0,
        };
        foreach (var e in entries)
        {
            payload.Entries.Add(new WebhookEntry
            {
                Database = e.Database,
                Type = e.Type.ToString(),
                Success = e.Success,
                OffsiteSuccess = e.OffsiteSuccess,
                File = e.FilePath,
                SizeBytes = e.FileSizeBytes,
                DurationSeconds = e.DurationSeconds,
                Error = e.Error,
            });
        }
        return payload;
    }

    public static WebhookPayload BuildAlertPayload(string eventName, string headline, IReadOnlyList<string> details) => new()
    {
        Event = eventName,
        Text = $"⚠️ {headline}" + (details.Count > 0 ? "\n" + string.Join("\n", details) : ""),
        Content = $"⚠️ {headline}" + (details.Count > 0 ? "\n" + string.Join("\n", details) : ""),
        Success = false,
        Details = details.ToList(),
    };

    public static async Task SendAsync(string url, WebhookPayload payload, CancellationToken ct = default)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(payload, JsonDefaults.Compact), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();
    }
}
