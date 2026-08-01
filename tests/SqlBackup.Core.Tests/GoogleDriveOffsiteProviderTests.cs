using SqlBackup.Core.Models;
using SqlBackup.Core.Offsite;
using SqlBackup.Core.Security;
using Xunit;

namespace SqlBackup.Core.Tests;

/// <summary>
/// Drive's network surface needs real credentials, so these cover the parts that
/// are ours: key/path translation, query escaping, and credential validation.
/// </summary>
public class GoogleDriveOffsiteProviderTests
{
    [Theory]
    [InlineData("srv1/Sales/Sales_Full_20260719_020000.bak", "srv1/Sales", "Sales_Full_20260719_020000.bak")]
    [InlineData("Sales/file.bak", "Sales", "file.bak")]
    [InlineData("file.bak", "", "file.bak")]
    [InlineData("/leading/slash.bak", "leading", "slash.bak")]
    public void SplitKey_SeparatesFolderPathFromFileName(string key, string folder, string file)
    {
        var (folderPath, fileName) = GoogleDriveOffsiteProvider.SplitKey(key);

        Assert.Equal(folder, folderPath);
        Assert.Equal(file, fileName);
    }

    [Theory]
    [InlineData("plain.bak", "plain.bak")]
    [InlineData("it's here.bak", @"it\'s here.bak")]
    [InlineData(@"back\slash.bak", @"back\\slash.bak")]
    public void QueryValues_AreEscapedForDriveSearchSyntax(string value, string expected)
    {
        // An unescaped apostrophe would terminate the query literal and break listing
        // (and therefore retention) for databases whose names contain one.
        Assert.Equal(expected, GoogleDriveOffsiteProvider.EscapeQueryValue(value));
    }

    [Fact]
    public void Factory_RequiresFolderId()
    {
        var destination = new OffsiteDestination
        {
            Name = "drive",
            Kind = OffsiteKind.GoogleDrive,
            GoogleAuthMode = GoogleDriveAuthMode.ServiceAccount,
            ProtectedGoogleServiceAccountJson = "dpapi:not-used-here",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => OffsiteProviderFactory.Create(destination, new SecretProtector()));
        Assert.Contains("folder ID", ex.Message);
    }

    [Fact]
    public void Factory_RejectsUnauthorizedOAuthDestination()
    {
        var destination = new OffsiteDestination
        {
            Name = "drive",
            Kind = OffsiteKind.GoogleDrive,
            GoogleAuthMode = GoogleDriveAuthMode.OAuthUser,
            GoogleFolderId = "abc123",
            GoogleClientId = "id.apps.googleusercontent.com",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => OffsiteProviderFactory.Create(destination, new SecretProtector()));
        Assert.Contains("not authorized", ex.Message);
    }

    [Fact]
    public void Factory_RejectsServiceAccountWithoutKey()
    {
        var destination = new OffsiteDestination
        {
            Name = "drive",
            Kind = OffsiteKind.GoogleDrive,
            GoogleAuthMode = GoogleDriveAuthMode.ServiceAccount,
            GoogleFolderId = "abc123",
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => OffsiteProviderFactory.Create(destination, new SecretProtector()));
        Assert.Contains("service account key", ex.Message);
    }

    [Fact]
    public void DescribeTarget_NamesTheFolderAndIdentity()
    {
        var serviceAccount = new OffsiteDestination
        {
            Kind = OffsiteKind.GoogleDrive,
            GoogleAuthMode = GoogleDriveAuthMode.ServiceAccount,
            GoogleFolderId = "abc123",
        };
        Assert.Equal("Google Drive folder abc123 (service account)", serviceAccount.DescribeTarget());

        var user = new OffsiteDestination
        {
            Kind = OffsiteKind.GoogleDrive,
            GoogleAuthMode = GoogleDriveAuthMode.OAuthUser,
            GoogleFolderId = "abc123",
            GoogleAuthorizedAccount = "dba@example.com",
        };
        Assert.Equal("Google Drive folder abc123 (dba@example.com)", user.DescribeTarget());
    }
}
