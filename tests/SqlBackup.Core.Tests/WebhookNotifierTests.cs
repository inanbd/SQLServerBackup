using System.Text.Json;
using SqlBackup.Core.Config;
using SqlBackup.Core.Models;
using SqlBackup.Core.Notifications;
using Xunit;

namespace SqlBackup.Core.Tests;

public class WebhookNotifierTests
{
    [Fact]
    public void JobPayload_CarriesTextAndStructuredEntries()
    {
        var entries = new List<JobHistoryEntry>
        {
            new() { Database = "Sales", Type = BackupType.Full, Success = true, FileSizeBytes = 42 },
            new() { Database = "Inv", Type = BackupType.Full, Success = false, Error = "boom" },
        };

        var payload = WebhookNotifier.BuildJobPayload("Nightly", entries);

        Assert.Equal("backup-result", payload.Event);
        Assert.False(payload.Success);
        Assert.Contains("FAILED", payload.Text);
        Assert.Contains("Nightly", payload.Text);
        Assert.Equal(payload.Text, payload.Content); // Discord compatibility duplicate
        Assert.Equal(2, payload.Entries.Count);
        Assert.Equal("boom", payload.Entries[1].Error);

        // Wire form uses camelCase so Slack's {"text": ...} contract is met.
        var wire = JsonSerializer.Serialize(payload, JsonDefaults.Compact);
        Assert.Contains("\"text\":", wire);
        Assert.Contains("\"content\":", wire);
    }

    [Fact]
    public void OffsiteFailure_MakesPayloadFailed()
    {
        var entries = new List<JobHistoryEntry>
        {
            new() { Database = "Sales", Success = true, OffsiteSuccess = false },
        };

        var payload = WebhookNotifier.BuildJobPayload("j", entries);

        Assert.False(payload.Success);
    }

    [Fact]
    public void AlertPayload_ListsDetails()
    {
        var payload = WebhookNotifier.BuildAlertPayload("rpo-breach", "2 targets overdue", new[] { "a", "b" });

        Assert.Equal("rpo-breach", payload.Event);
        Assert.Contains("a", payload.Details);
        Assert.Contains("2 targets overdue", payload.Text);
    }
}
