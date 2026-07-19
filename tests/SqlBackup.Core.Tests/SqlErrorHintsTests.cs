using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class SqlErrorHintsTests
{
    private const string SystemAccount = @"NT AUTHORITY\SYSTEM";

    [Theory]
    [InlineData(916)]   // principal cannot access db under current security context
    [InlineData(262)]   // BACKUP DATABASE permission denied
    [InlineData(4060)]  // cannot open database requested by the login
    [InlineData(18456)] // login failed
    public void PermissionErrors_WithWindowsAuth_ExplainServiceAccount(int errorNumber)
    {
        var hint = SqlErrorHints.ForBackupFailure(new[] { errorNumber }, SqlAuthMode.Windows, SystemAccount);

        Assert.NotNull(hint);
        Assert.Contains(SystemAccount, hint);
        Assert.Contains("db_backupoperator", hint);
        Assert.Contains("services.msc", hint);
    }

    [Fact]
    public void PermissionErrors_WithSqlAuth_PointAtTheLogin_NotTheServiceAccount()
    {
        var hint = SqlErrorHints.ForBackupFailure(new[] { 262 }, SqlAuthMode.Sql, SystemAccount);

        Assert.NotNull(hint);
        Assert.Contains("db_backupoperator", hint);
        Assert.DoesNotContain("services.msc", hint);
    }

    [Fact]
    public void UnrelatedErrors_GetNoIdentityLecture()
    {
        // 3201: cannot open backup device (path problem) — identity guidance would mislead.
        Assert.Null(SqlErrorHints.ForBackupFailure(new[] { 3201 }, SqlAuthMode.Windows, SystemAccount));
        Assert.Null(SqlErrorHints.ForBackupFailure(Array.Empty<int>(), SqlAuthMode.Windows, SystemAccount));
    }

    [Fact]
    public void LoginFailure_IsDetectedForRetrySuppression()
    {
        Assert.True(SqlErrorHints.IsLoginFailure(new[] { 18456 }));
        Assert.False(SqlErrorHints.IsLoginFailure(new[] { 916, 262 }));
    }

    [Fact]
    public void CurrentProcessAccount_LooksLikeDomainQualifiedName()
    {
        Assert.Contains("\\", SqlErrorHints.CurrentProcessAccount);
    }
}
