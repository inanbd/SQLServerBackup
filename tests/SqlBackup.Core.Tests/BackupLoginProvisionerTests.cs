using System.Text.RegularExpressions;
using SqlBackup.Core.Backup;
using Xunit;

namespace SqlBackup.Core.Tests;

public class BackupLoginProvisionerTests
{
    [Theory]
    [InlineData("Production 01!", "iSQLBackup_Produ")]
    [InlineData("db", "iSQLBackup_db")]
    [InlineData("  Prod-Server #1  ", "iSQLBackup_ProdS")]
    [InlineData("!!! ***", "iSQLBackup_Conn")] // nothing usable -> fallback
    public void BuildBaseName_StripsSpecialsAndTakesFiveChars(string title, string expected)
    {
        Assert.Equal(expected, BackupLoginProvisioner.BuildBaseName(title));
    }

    [Fact]
    public void GenerateUserName_MatchesRequiredFormat()
    {
        var name = BackupLoginProvisioner.GenerateUserName("Production 01");

        Assert.Matches(new Regex(@"^iSQLBackup_Produ_\d{5}$"), name);
    }

    [Fact]
    public void GenerateUserName_RandomSuffixVaries()
    {
        var names = Enumerable.Range(0, 20)
            .Select(_ => BackupLoginProvisioner.GenerateUserName("Same"))
            .Distinct()
            .Count();

        Assert.True(names > 1, "5-digit suffix should vary between generations");
    }

    [Fact]
    public void GeneratePassword_IsStrongAndSqlSafe()
    {
        var password = BackupLoginProvisioner.GeneratePassword();

        Assert.Equal(24, password.Length);
        Assert.Contains(password, char.IsAsciiLetterUpper);
        Assert.Contains(password, char.IsAsciiLetterLower);
        Assert.Contains(password, char.IsAsciiDigit);
        Assert.Contains(password, c => !char.IsAsciiLetterOrDigit(c));
        Assert.DoesNotContain('\'', password);
        Assert.DoesNotContain('"', password);
        Assert.DoesNotContain(';', password);
        Assert.NotEqual(password, BackupLoginProvisioner.GeneratePassword());
    }

    [Fact]
    public void ExistingLoginPattern_EscapesUnderscores_AndPinsFiveDigits()
    {
        var pattern = BackupLoginProvisioner.BuildExistingLoginPattern("iSQLBackup_Produ");

        Assert.Equal(@"iSQLBackup\_Produ\_[0-9][0-9][0-9][0-9][0-9]", pattern);
    }

    [Fact]
    public void DatabaseGrantScript_MapsUserAndBackupRole_Idempotently()
    {
        var script = BackupLoginProvisioner.BuildDatabaseGrantScript("Admin_Elara", "iSQLBackup_Produ_12345", includeHistoryRead: false);

        Assert.Contains("USE [Admin_Elara];", script);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'iSQLBackup_Produ_12345')", script);
        Assert.Contains("CREATE USER [iSQLBackup_Produ_12345] FOR LOGIN [iSQLBackup_Produ_12345];", script);
        Assert.Contains("ALTER ROLE [db_backupoperator] ADD MEMBER [iSQLBackup_Produ_12345];", script);
        Assert.DoesNotContain("db_datareader", script);
    }

    [Fact]
    public void DatabaseGrantScript_ForMsdb_AddsHistoryRead()
    {
        var script = BackupLoginProvisioner.BuildDatabaseGrantScript("msdb", "iSQLBackup_x_11111", includeHistoryRead: true);

        Assert.Contains("db_datareader", script);
    }

    [Fact]
    public void DatabaseGrantScript_EscapesHostileNames()
    {
        var script = BackupLoginProvisioner.BuildDatabaseGrantScript("we]ird db", "iSQLBackup_x_11111", false);

        Assert.Contains("USE [we]]ird db];", script);
    }
}
