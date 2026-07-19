using SqlBackup.Core.History;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class HistoryStoreTests
{
    private static JobHistoryEntry Entry(Guid jobId, DateTimeOffset started, bool success = true) => new()
    {
        JobId = jobId,
        JobName = "job",
        Database = "Db",
        StartedUtc = started,
        Success = success,
    };

    [Fact]
    public void ReadRecent_ReturnsNewestFirst_AndFiltersByJob()
    {
        using var dir = new TempDirectory();
        var store = new HistoryStore(dir.Path);
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        store.Append(Entry(jobA, now.AddMinutes(-30)));
        store.Append(Entry(jobB, now.AddMinutes(-20)));
        store.Append(Entry(jobA, now.AddMinutes(-10)));

        var all = store.ReadRecent(10);
        Assert.Equal(3, all.Count);
        Assert.Equal(now.AddMinutes(-10), all[0].StartedUtc, TimeSpan.FromSeconds(1));

        var onlyA = store.ReadRecent(10, jobA);
        Assert.Equal(2, onlyA.Count);
        Assert.All(onlyA, e => Assert.Equal(jobA, e.JobId));

        var capped = store.ReadRecent(1);
        Assert.Single(capped);
    }

    [Fact]
    public void GetLastEntryPerJob_PicksNewestPerJob()
    {
        using var dir = new TempDirectory();
        var store = new HistoryStore(dir.Path);
        var job = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        store.Append(Entry(job, now.AddHours(-2), success: true));
        store.Append(Entry(job, now.AddHours(-1), success: false));

        var last = store.GetLastEntryPerJob();

        Assert.False(last[job].Success);
    }

    [Fact]
    public void Rotation_KeepsEntriesReadable()
    {
        using var dir = new TempDirectory();
        var store = new HistoryStore(dir.Path, maxFileBytes: 500);
        var job = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 20; i++)
            store.Append(Entry(job, now.AddMinutes(i)));

        Assert.True(File.Exists(Path.Combine(dir.Path, "history.1.jsonl")));
        // Older rotations are discarded by design; what must hold is that the merged
        // read spans archive + current and the newest entry always survives.
        var recent = store.ReadRecent(100);
        Assert.True(recent.Count >= 2, $"expected rotated history to remain readable, got {recent.Count}");
        Assert.Equal(now.AddMinutes(19), recent[0].StartedUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LastSuccessMaps_IgnoreFailures_AndKeyDatabasesLowercase()
    {
        using var dir = new TempDirectory();
        var store = new HistoryStore(dir.Path);
        var job = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var ok = Entry(job, now.AddHours(-3), success: true);
        ok.Database = "Sales";
        store.Append(ok);
        store.Append(Entry(job, now.AddHours(-1), success: false));

        var perJob = store.GetLastSuccessPerJob();
        Assert.Equal(now.AddHours(-3), perJob[job], TimeSpan.FromSeconds(1));

        var perDb = store.GetLastSuccessPerJobDatabase();
        Assert.True(perDb.ContainsKey((job, "sales")));
    }

    [Fact]
    public void CorruptLines_AreSkipped()
    {
        using var dir = new TempDirectory();
        var store = new HistoryStore(dir.Path);
        store.Append(Entry(Guid.NewGuid(), DateTimeOffset.UtcNow));
        File.AppendAllText(Path.Combine(dir.Path, "history.jsonl"), "{ torn write");

        Assert.Single(store.ReadRecent(10));
    }
}
