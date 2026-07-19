using SqlBackup.Core.Models;
using SqlBackup.Core.Monitoring;
using Xunit;

namespace SqlBackup.Core.Tests;

public class RpoMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private static BackupJob ExplicitJob(int rpoHours, params string[] databases) => new()
    {
        Name = "job",
        RpoHours = rpoHours,
        Databases = databases.ToList(),
    };

    [Fact]
    public void FreshBackups_NoBreach()
    {
        var job = ExplicitJob(24, "Sales");
        var perDb = new Dictionary<(Guid, string), DateTimeOffset> { [(job.Id, "sales")] = Now.AddHours(-2) };

        var breaches = RpoMonitor.Evaluate(new[] { job }, new Dictionary<Guid, DateTimeOffset>(), perDb, Now);

        Assert.Empty(breaches);
    }

    [Fact]
    public void StaleAndNeverBackedUpDatabases_Breach_PerDatabase()
    {
        var job = ExplicitJob(24, "Sales", "Inventory");
        var perDb = new Dictionary<(Guid, string), DateTimeOffset>
        {
            [(job.Id, "sales")] = Now.AddHours(-30), // stale
            // Inventory: never
        };

        var breaches = RpoMonitor.Evaluate(new[] { job }, new Dictionary<Guid, DateTimeOffset>(), perDb, Now);

        Assert.Equal(2, breaches.Count);
        var sales = Assert.Single(breaches, b => b.Database == "Sales");
        Assert.NotNull(sales.LastSuccessUtc);
        var inventory = Assert.Single(breaches, b => b.Database == "Inventory");
        Assert.Null(inventory.LastSuccessUtc);
        Assert.Contains("no successful backup recorded at all", inventory.Describe());
    }

    [Fact]
    public void DisabledJob_WithRpo_StillBreaches()
    {
        // The whole point: a silently disabled job must not silence the safety net.
        var job = ExplicitJob(24, "Sales");
        job.Enabled = false;

        var breaches = RpoMonitor.Evaluate(new[] { job },
            new Dictionary<Guid, DateTimeOffset>(), new Dictionary<(Guid, string), DateTimeOffset>(), Now);

        Assert.Single(breaches);
    }

    [Fact]
    public void RpoZero_DisablesTheCheck()
    {
        var job = ExplicitJob(0, "Sales");

        var breaches = RpoMonitor.Evaluate(new[] { job },
            new Dictionary<Guid, DateTimeOffset>(), new Dictionary<(Guid, string), DateTimeOffset>(), Now);

        Assert.Empty(breaches);
    }

    [Fact]
    public void DiscoveryModeJobs_AreCheckedAtJobLevel()
    {
        var job = new BackupJob { Name = "all", RpoHours = 12, SelectionMode = DatabaseSelectionMode.AllUserDatabases };
        var perJob = new Dictionary<Guid, DateTimeOffset> { [job.Id] = Now.AddHours(-13) };

        var breaches = RpoMonitor.Evaluate(new[] { job }, perJob, new Dictionary<(Guid, string), DateTimeOffset>(), Now);

        var breach = Assert.Single(breaches);
        Assert.Null(breach.Database);

        perJob[job.Id] = Now.AddHours(-11);
        Assert.Empty(RpoMonitor.Evaluate(new[] { job }, perJob, new Dictionary<(Guid, string), DateTimeOffset>(), Now));
    }
}
