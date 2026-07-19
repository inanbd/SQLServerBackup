using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class ChainAwareRetentionTests
{
    private static readonly DateTime Now = new(2026, 7, 19, 12, 0, 0);

    private static RetentionFile Full(int day) => new($"Db_Full_{day}", "Full", Now.AddDays(-30 + day));
    private static RetentionFile Diff(int day, double hours = 0) => new($"Db_Diff_{day}", "Diff", Now.AddDays(-30 + day).AddHours(hours));

    private static RetentionPolicy KeepLast(int n) => new() { Mode = RetentionMode.KeepLastN, KeepLast = n };

    [Fact]
    public void FullWithDependentDiff_IsSpared()
    {
        // F(1), D(2), F(3); keep 1 full -> F(1) would be doomed, but D(2) is based on it.
        var files = new List<RetentionFile> { Full(1), Diff(2), Full(3) };

        var (delete, keptForChain) = RetentionEnforcer.Plan(files, BackupType.Full, KeepLast(1), Now);

        Assert.Empty(delete);
        Assert.Single(keptForChain, f => f.Key == "Db_Full_1");
    }

    [Fact]
    public void FullWithoutDependents_IsDeleted()
    {
        // F(1), F(3), D(4): the diff is based on F(3); F(1) has no dependents.
        var files = new List<RetentionFile> { Full(1), Full(3), Diff(4) };

        var (delete, keptForChain) = RetentionEnforcer.Plan(files, BackupType.Full, KeepLast(1), Now);

        Assert.Single(delete, f => f.Key == "Db_Full_1");
        Assert.Empty(keptForChain);
    }

    [Fact]
    public void MiddleFullWithDependent_SparedButOlderOneStillDeleted()
    {
        // F(1), F(2), D(3), F(4); keep 1 -> doomed {F2, F1}. D(3) depends on F(2): spare it.
        // F(1) then has no dependent in (1,2): delete.
        var files = new List<RetentionFile> { Full(1), Full(2), Diff(3), Full(4) };

        var (delete, keptForChain) = RetentionEnforcer.Plan(files, BackupType.Full, KeepLast(1), Now);

        Assert.Single(delete, f => f.Key == "Db_Full_1");
        Assert.Single(keptForChain, f => f.Key == "Db_Full_2");
    }

    [Fact]
    public void DiffRetention_IsNotChainGuarded()
    {
        // Deleting old diffs never breaks newer restore points.
        var files = new List<RetentionFile> { Full(1), Diff(2), Diff(3), Diff(4) };

        var (delete, keptForChain) = RetentionEnforcer.Plan(files, BackupType.Differential, KeepLast(1), Now);

        Assert.Equal(2, delete.Count);
        Assert.Empty(keptForChain);
        Assert.DoesNotContain(delete, f => f.TypeToken == "Full");
    }

    [Fact]
    public void ApplyOnDisk_SparesChainAndReportsIt()
    {
        using var dir = new TempDirectory();
        string Drop(BackupType type, DateTime ts)
        {
            var path = Path.Combine(dir.Path, BackupFileNamer.BuildFileName("Db", type, ts));
            File.WriteAllText(path, "x");
            return path;
        }
        var oldFull = Drop(BackupType.Full, Now.AddDays(-10));
        Drop(BackupType.Differential, Now.AddDays(-9));
        var newFull = Drop(BackupType.Full, Now.AddDays(-1));

        var outcome = RetentionEnforcer.Apply(dir.Path, "Db", BackupType.Full, KeepLast(1), Now);

        Assert.Empty(outcome.DeletedFiles);
        Assert.Single(outcome.KeptForChain, oldFull);
        Assert.True(File.Exists(oldFull));
        Assert.True(File.Exists(newFull));
    }
}
