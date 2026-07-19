using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class BackupFileNamerTests
{
    [Fact]
    public void BuildFileName_RoundTripsThroughTryParse()
    {
        var stamp = new DateTime(2026, 7, 18, 14, 30, 45);
        var name = BackupFileNamer.BuildFileName("Sales", BackupType.Full, stamp);

        Assert.Equal("Sales_Full_20260718_143045.bak", name);
        Assert.True(BackupFileNamer.TryParse(name, out var db, out var type, out var parsed));
        Assert.Equal("Sales", db);
        Assert.Equal("Full", type);
        Assert.Equal(stamp, parsed);
    }

    [Fact]
    public void LogBackups_GetTrnExtension()
    {
        var name = BackupFileNamer.BuildFileName("Db", BackupType.TransactionLog, new DateTime(2026, 1, 2, 3, 4, 5));
        Assert.EndsWith(".trn", name);
        Assert.Contains("_Log_", name);
    }

    [Fact]
    public void DatabaseNames_WithInvalidPathChars_AreSanitized()
    {
        var name = BackupFileNamer.BuildFileName("My/Db:Prod", BackupType.Full, new DateTime(2026, 1, 2, 3, 4, 5));
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
        Assert.StartsWith("My_Db_Prod_Full_", name);
    }

    [Theory]
    [InlineData("random.txt")]
    [InlineData("Sales_Full_2026.bak")]
    [InlineData("Sales_Whatever_20260718_143045.bak")]
    [InlineData("Sales_Full_20261399_143045.bak")] // impossible date
    public void ForeignFiles_AreNotParsed(string fileName)
    {
        Assert.False(BackupFileNamer.TryParse(fileName, out _, out _, out _));
    }

    [Fact]
    public void DatabaseNames_ContainingUnderscores_StillParse()
    {
        var name = BackupFileNamer.BuildFileName("My_Fine_Db", BackupType.Differential, new DateTime(2026, 7, 18, 1, 2, 3));
        Assert.True(BackupFileNamer.TryParse(name, out var db, out var type, out _));
        Assert.Equal("My_Fine_Db", db);
        Assert.Equal("Diff", type);
    }
}
