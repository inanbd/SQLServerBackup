using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class RetentionEnforcerTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 12, 0, 0);

    private static string Drop(TempDirectory dir, string db, BackupType type, DateTime stamp)
    {
        var path = Path.Combine(dir.Path, BackupFileNamer.BuildFileName(db, type, stamp));
        File.WriteAllText(path, "backup");
        return path;
    }

    [Fact]
    public void KeepLastN_DeletesOldestBeyondN()
    {
        using var dir = new TempDirectory();
        var oldest = Drop(dir, "Db", BackupType.Full, Now.AddDays(-3));
        var middle = Drop(dir, "Db", BackupType.Full, Now.AddDays(-2));
        var newest = Drop(dir, "Db", BackupType.Full, Now.AddDays(-1));

        var outcome = RetentionEnforcer.Apply(dir.Path, "Db", BackupType.Full,
            new RetentionPolicy { Mode = RetentionMode.KeepLastN, KeepLast = 2 }, Now);

        Assert.Single(outcome.DeletedFiles, oldest);
        Assert.False(File.Exists(oldest));
        Assert.True(File.Exists(middle));
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void OtherDatabasesAndTypes_AreNeverTouched()
    {
        using var dir = new TempDirectory();
        Drop(dir, "Db", BackupType.Full, Now.AddDays(-5));
        var otherDb = Drop(dir, "OtherDb", BackupType.Full, Now.AddDays(-50));
        var otherType = Drop(dir, "Db", BackupType.TransactionLog, Now.AddDays(-50));
        var foreign = Path.Combine(dir.Path, "do-not-touch.bak");
        File.WriteAllText(foreign, "not ours");

        RetentionEnforcer.Apply(dir.Path, "Db", BackupType.Full,
            new RetentionPolicy { Mode = RetentionMode.KeepLastN, KeepLast = 1 }, Now);

        Assert.True(File.Exists(otherDb));
        Assert.True(File.Exists(otherType));
        Assert.True(File.Exists(foreign));
    }

    [Fact]
    public void MaxAgeDays_DeletesExpired_ButAlwaysKeepsNewest()
    {
        using var dir = new TempDirectory();
        var ancient1 = Drop(dir, "Db", BackupType.Full, Now.AddDays(-100));
        var ancient2 = Drop(dir, "Db", BackupType.Full, Now.AddDays(-99));

        var outcome = RetentionEnforcer.Apply(dir.Path, "Db", BackupType.Full,
            new RetentionPolicy { Mode = RetentionMode.MaxAgeDays, MaxAgeDays = 30 }, Now);

        // Both are expired, but the newest survivor rule keeps ancient2.
        Assert.Single(outcome.DeletedFiles, ancient1);
        Assert.True(File.Exists(ancient2));
    }

    [Fact]
    public void KeepLastZero_StillKeepsOne()
    {
        using var dir = new TempDirectory();
        Drop(dir, "Db", BackupType.Full, Now.AddDays(-2));
        var newest = Drop(dir, "Db", BackupType.Full, Now.AddDays(-1));

        RetentionEnforcer.Apply(dir.Path, "Db", BackupType.Full,
            new RetentionPolicy { Mode = RetentionMode.KeepLastN, KeepLast = 0 }, Now);

        Assert.True(File.Exists(newest));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void KeepAll_DeletesNothing()
    {
        using var dir = new TempDirectory();
        Drop(dir, "Db", BackupType.Full, Now.AddDays(-1000));

        var outcome = RetentionEnforcer.Apply(dir.Path, "Db", BackupType.Full,
            new RetentionPolicy { Mode = RetentionMode.KeepAll }, Now);

        Assert.Empty(outcome.DeletedFiles);
        Assert.Single(Directory.GetFiles(dir.Path));
    }
}
