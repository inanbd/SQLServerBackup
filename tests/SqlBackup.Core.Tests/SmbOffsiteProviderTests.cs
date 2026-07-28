using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using SqlBackup.Core.Offsite;
using SqlBackup.Core.Security;
using Xunit;

namespace SqlBackup.Core.Tests;

/// <summary>
/// The SMB provider is plain file I/O against a share path, so a temp directory
/// exercises the real code path on any platform (credential handling is the only
/// Windows-specific part and is covered separately).
/// </summary>
public class SmbOffsiteProviderTests
{
    [Theory]
    [InlineData(@"\\nas\backups\sql\daily", @"\\nas\backups")]
    [InlineData(@"\\nas\backups", @"\\nas\backups")]
    [InlineData(@"\\nas", @"\\nas")]
    [InlineData(@"D:\local\path", @"D:\local\path")]
    public void ShareRoot_IsTheConnectableUnit(string path, string expected)
    {
        Assert.Equal(expected, SmbOffsiteProvider.GetShareRoot(path));
    }

    [Fact]
    public async Task Upload_List_Delete_RoundTrip()
    {
        using var share = new TempDirectory();
        using var staging = new TempDirectory();
        var local = Path.Combine(staging.Path, "Sales_Full_20260719_020000.bak");
        await File.WriteAllTextAsync(local, "backup-bytes");

        using var provider = new SmbOffsiteProvider(share.Path, null, null);
        await provider.TestAsync();

        await provider.UploadAsync(local, "srv1/Sales/Sales_Full_20260719_020000.bak");

        var uploaded = Path.Combine(share.Path, "srv1", "Sales", "Sales_Full_20260719_020000.bak");
        Assert.True(File.Exists(uploaded));
        Assert.Equal("backup-bytes", await File.ReadAllTextAsync(uploaded));

        var keys = await provider.ListKeysAsync("srv1/Sales/");
        Assert.Single(keys, "srv1/Sales/Sales_Full_20260719_020000.bak");

        await provider.DeleteAsync("srv1/Sales/Sales_Full_20260719_020000.bak");
        Assert.False(File.Exists(uploaded));
    }

    [Fact]
    public async Task Upload_OverwritesAndLeavesNoStagingFile()
    {
        using var share = new TempDirectory();
        using var staging = new TempDirectory();
        var local = Path.Combine(staging.Path, "Db_Full_20260719_020000.bak");

        using var provider = new SmbOffsiteProvider(share.Path, null, null);

        await File.WriteAllTextAsync(local, "first");
        await provider.UploadAsync(local, "Db_Full_20260719_020000.bak");
        await File.WriteAllTextAsync(local, "second");
        await provider.UploadAsync(local, "Db_Full_20260719_020000.bak");

        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(share.Path, "Db_Full_20260719_020000.bak")));
        Assert.Empty(Directory.GetFiles(share.Path, "*.uploading"));
    }

    [Fact]
    public async Task ListKeys_OnMissingFolder_IsEmpty()
    {
        using var share = new TempDirectory();
        using var provider = new SmbOffsiteProvider(share.Path, null, null);

        Assert.Empty(await provider.ListKeysAsync("nope/"));
    }

    [Fact]
    public async Task RetentionThroughUploader_WorksOverAShare()
    {
        using var share = new TempDirectory();
        using var staging = new TempDirectory();
        var now = new DateTime(2026, 7, 19, 12, 0, 0);

        using var provider = new SmbOffsiteProvider(share.Path, null, null);
        var uploader = new OffsiteUploader(provider, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        // Three daily fulls uploaded in order, keeping the last two.
        foreach (var daysAgo in new[] { 2, 1, 0 })
        {
            var stamp = now.AddDays(-daysAgo);
            var local = Path.Combine(staging.Path, BackupFileNamer.BuildFileName("Db", BackupType.Full, stamp));
            await File.WriteAllTextAsync(local, "x");
            var result = await uploader.ProcessAsync(local, "Db", subfolderPerDatabase: true, BackupType.Full,
                new RetentionPolicy { Mode = RetentionMode.KeepLastN, KeepLast = 2 }, null, stamp, CancellationToken.None);
            Assert.True(result.Success);
        }

        var remaining = Directory.GetFiles(Path.Combine(share.Path, "Db"));
        Assert.Equal(2, remaining.Length);
        Assert.DoesNotContain(remaining, f => f.Contains(now.AddDays(-2).ToString("yyyyMMdd")));
    }

    [Fact]
    public void Factory_BuildsShareProvider_WithoutCredentials()
    {
        using var share = new TempDirectory();
        var destination = new OffsiteDestination
        {
            Name = "nas",
            Kind = OffsiteKind.SmbShare,
            SmbPath = share.Path,
        };

        using var provider = OffsiteProviderFactory.Create(destination, new SecretProtector());

        Assert.Equal(share.Path.TrimEnd(Path.DirectorySeparatorChar), provider.Describe());
    }

    [Fact]
    public void Factory_RequiresSharePath()
    {
        var destination = new OffsiteDestination { Name = "nas", Kind = OffsiteKind.SmbShare };

        var ex = Assert.Throws<InvalidOperationException>(
            () => OffsiteProviderFactory.Create(destination, new SecretProtector()));
        Assert.Contains("share path", ex.Message);
    }
}
