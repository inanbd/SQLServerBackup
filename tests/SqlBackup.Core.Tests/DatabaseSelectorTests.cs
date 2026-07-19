using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class DatabaseSelectorTests
{
    private static readonly string[] OnServer = { "master", "model", "msdb", "Sales", "Inventory", "Scratch" };

    [Fact]
    public void Explicit_ReturnsConfiguredListVerbatim()
    {
        var result = DatabaseSelector.Resolve(
            DatabaseSelectionMode.Explicit, new[] { "Sales", "GoneDb" }, new[] { "ignored" }, null);

        Assert.Equal(new[] { "Sales", "GoneDb" }, result);
    }

    [Fact]
    public void AllUserDatabases_SkipsSystemDbs_AndExclusions_CaseInsensitively()
    {
        var result = DatabaseSelector.Resolve(
            DatabaseSelectionMode.AllUserDatabases, Array.Empty<string>(), new[] { "SCRATCH" }, OnServer);

        Assert.Equal(new[] { "Sales", "Inventory" }, result);
    }

    [Fact]
    public void AllDatabases_IncludesSystemDbs()
    {
        var result = DatabaseSelector.Resolve(
            DatabaseSelectionMode.AllDatabases, Array.Empty<string>(), new[] { "model" }, OnServer);

        Assert.Contains("master", result);
        Assert.Contains("msdb", result);
        Assert.DoesNotContain("model", result);
    }

    [Fact]
    public void Describe_ShowsModeAndExclusions()
    {
        var job = new BackupJob
        {
            SelectionMode = DatabaseSelectionMode.AllUserDatabases,
            ExcludedDatabases = { "Scratch" },
        };
        Assert.Equal("All user databases (except Scratch)", DatabaseSelector.Describe(job));

        var explicitJob = new BackupJob { Databases = { "A", "B" } };
        Assert.Equal("A, B", DatabaseSelector.Describe(explicitJob));
    }
}
