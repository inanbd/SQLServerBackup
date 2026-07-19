using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class BackupScriptBuilderTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 7, 18, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FullBackup_UsesBackupDatabaseVerb()
    {
        var sql = BackupScriptBuilder.BuildBackupCommand(
            "Sales", BackupType.Full, @"D:\Backups\Sales_Full_20260718_020000.bak", new BackupJobOptions(), Stamp);

        Assert.StartsWith("BACKUP DATABASE [Sales] TO DISK = N'D:\\Backups\\Sales_Full_20260718_020000.bak'", sql);
        Assert.Contains("INIT", sql);
        Assert.Contains("FORMAT", sql);
        Assert.Contains("CHECKSUM", sql); // default option
        Assert.Contains("STATS = 10", sql);
        Assert.DoesNotContain("DIFFERENTIAL", sql);
    }

    [Fact]
    public void DifferentialBackup_AddsDifferentialClause()
    {
        var sql = BackupScriptBuilder.BuildBackupCommand(
            "Sales", BackupType.Differential, @"D:\b\f.bak", new BackupJobOptions(), Stamp);

        Assert.StartsWith("BACKUP DATABASE [Sales]", sql);
        Assert.Contains("DIFFERENTIAL", sql);
    }

    [Fact]
    public void LogBackup_UsesBackupLogVerb()
    {
        var sql = BackupScriptBuilder.BuildBackupCommand(
            "Sales", BackupType.TransactionLog, @"D:\b\f.trn", new BackupJobOptions(), Stamp);

        Assert.StartsWith("BACKUP LOG [Sales]", sql);
    }

    [Fact]
    public void DatabaseName_WithBracketAndQuote_IsEscaped()
    {
        var sql = BackupScriptBuilder.BuildBackupCommand(
            "we]ird'db", BackupType.Full, @"D:\it's here\f.bak", new BackupJobOptions(), Stamp);

        Assert.Contains("[we]]ird'db]", sql);
        Assert.Contains(@"N'D:\it''s here\f.bak'", sql);
    }

    [Fact]
    public void Options_TogglesAppearInScript()
    {
        var options = new BackupJobOptions { Compression = true, Checksum = false, CopyOnly = true };
        var sql = BackupScriptBuilder.BuildBackupCommand("Db", BackupType.Full, "f.bak", options, Stamp);

        Assert.Contains("COMPRESSION", sql);
        Assert.Contains("COPY_ONLY", sql);
        Assert.DoesNotContain("CHECKSUM", sql);
    }

    [Fact]
    public void CopyOnly_IsIgnoredForDifferentials()
    {
        var options = new BackupJobOptions { CopyOnly = true };
        var sql = BackupScriptBuilder.BuildBackupCommand("Db", BackupType.Differential, "f.bak", options, Stamp);

        Assert.DoesNotContain("COPY_ONLY", sql);
    }

    [Fact]
    public void VerifyCommand_RespectsChecksumFlag()
    {
        Assert.Equal("RESTORE VERIFYONLY FROM DISK = N'f.bak' WITH CHECKSUM",
            BackupScriptBuilder.BuildVerifyCommand("f.bak", checksum: true));
        Assert.Equal("RESTORE VERIFYONLY FROM DISK = N'f.bak'",
            BackupScriptBuilder.BuildVerifyCommand("f.bak", checksum: false));
    }
}
