using Microsoft.Extensions.Logging.Abstractions;
using SqlBackup.Core.Backup;
using SqlBackup.Core.Models;
using SqlBackup.Core.Offsite;
using Xunit;

namespace SqlBackup.Core.Tests;

/// <summary>In-memory provider: dictionary of key -> content, with optional injected upload failures.</summary>
internal sealed class FakeOffsiteProvider : IOffsiteProvider
{
    public Dictionary<string, byte[]> Files { get; } = new();
    public int FailUploadsRemaining { get; set; }
    public int UploadAttempts { get; private set; }

    public string Describe() => "fake://unit-test";

    public Task TestAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task UploadAsync(string localFilePath, string remoteKey, CancellationToken ct = default)
    {
        UploadAttempts++;
        if (FailUploadsRemaining > 0)
        {
            FailUploadsRemaining--;
            throw new IOException("simulated upload failure");
        }
        Files[remoteKey] = File.ReadAllBytes(localFilePath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(string keyPrefix, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Files.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)).ToList());

    public Task DeleteAsync(string remoteKey, CancellationToken ct = default)
    {
        Files.Remove(remoteKey);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

public class OffsiteUploaderTests
{
    private static readonly DateTime Now = new(2026, 7, 19, 12, 0, 0);

    private static string DropLocal(TempDirectory dir, string database, BackupType type, DateTime ts)
    {
        var path = Path.Combine(dir.Path, BackupFileNamer.BuildFileName(database, type, ts));
        File.WriteAllText(path, "backup-bytes");
        return path;
    }

    [Theory]
    [InlineData("srv1", "Sales", "f.bak", "srv1/Sales/f.bak")]
    [InlineData(null, "Sales", "f.bak", "Sales/f.bak")]
    [InlineData("p/", null, "f.bak", "p/f.bak")]
    public void RemoteKeys_JoinCleanly(string? prefix, string? subfolder, string file, string expected)
    {
        Assert.Equal(expected, OffsiteUploader.BuildRemoteKey(prefix, subfolder, file));
    }

    [Fact]
    public async Task Upload_StoresFile_UnderDatabaseSubfolder()
    {
        using var dir = new TempDirectory();
        var provider = new FakeOffsiteProvider();
        var uploader = new OffsiteUploader(provider, NullLogger.Instance);
        var local = DropLocal(dir, "Sales", BackupType.Full, Now);

        var result = await uploader.ProcessAsync(local, "Sales", subfolderPerDatabase: true, BackupType.Full,
            new RetentionPolicy { Mode = RetentionMode.KeepAll }, destinationPrefix: "srv1", Now, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains($"srv1/Sales/{Path.GetFileName(local)}", provider.Files.Keys);
        Assert.Contains(result.Notes, n => n.Contains("uploaded"));
    }

    private static readonly TimeSpan[] NoDelays = { TimeSpan.Zero, TimeSpan.Zero };

    [Fact]
    public async Task TransientFailure_IsRetried_ThenSucceeds()
    {
        using var dir = new TempDirectory();
        var provider = new FakeOffsiteProvider { FailUploadsRemaining = 1 };
        var uploader = new OffsiteUploader(provider, NullLogger.Instance, NoDelays);
        var local = DropLocal(dir, "Db", BackupType.Full, Now);

        var result = await uploader.ProcessAsync(local, "Db", false, BackupType.Full,
            new RetentionPolicy { Mode = RetentionMode.KeepAll }, null, Now, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, provider.UploadAttempts);
    }

    [Fact]
    public async Task PersistentFailure_ReportsFailure_WithNote()
    {
        using var dir = new TempDirectory();
        var provider = new FakeOffsiteProvider { FailUploadsRemaining = 99 };
        var uploader = new OffsiteUploader(provider, NullLogger.Instance, NoDelays);
        var local = DropLocal(dir, "Db", BackupType.Full, Now);

        var result = await uploader.ProcessAsync(local, "Db", false, BackupType.Full,
            new RetentionPolicy { Mode = RetentionMode.KeepAll }, null, Now, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Notes, n => n.Contains("FAILED"));
    }

    [Fact]
    public async Task RemoteRetention_DeletesOldMatchingFiles_KeepsForeignAndChainFiles()
    {
        using var dir = new TempDirectory();
        var provider = new FakeOffsiteProvider();
        // Pre-existing remote content. With keep-last-2 fulls (plus today's upload), both
        // -10d and -9d are policy-doomed, but the -8.5d diff depends on the -9d full.
        provider.Files[$"Db/{BackupFileNamer.BuildFileName("Db", BackupType.Full, Now.AddDays(-10))}"] = new byte[1];
        provider.Files[$"Db/{BackupFileNamer.BuildFileName("Db", BackupType.Full, Now.AddDays(-9))}"] = new byte[1];
        provider.Files[$"Db/{BackupFileNamer.BuildFileName("Db", BackupType.Differential, Now.AddDays(-9).AddHours(12))}"] = new byte[1];
        provider.Files[$"Db/{BackupFileNamer.BuildFileName("Db", BackupType.Full, Now.AddDays(-2))}"] = new byte[1];
        provider.Files["Db/unrelated.txt"] = new byte[1];

        var uploader = new OffsiteUploader(provider, NullLogger.Instance);
        var local = DropLocal(dir, "Db", BackupType.Full, Now);

        var result = await uploader.ProcessAsync(local, "Db", subfolderPerDatabase: true, BackupType.Full,
            new RetentionPolicy { Mode = RetentionMode.KeepLastN, KeepLast = 2 }, null, Now, CancellationToken.None);

        Assert.True(result.Success);
        // -10d full deleted (no dependents); -9d full spared for its diff; foreign file untouched.
        Assert.DoesNotContain(provider.Files.Keys, k => k.Contains("Full_" + Now.AddDays(-10).ToString("yyyyMMdd")));
        Assert.Contains(provider.Files.Keys, k => k.Contains("Full_" + Now.AddDays(-9).ToString("yyyyMMdd")));
        Assert.Contains("Db/unrelated.txt", provider.Files.Keys);
        Assert.Contains(result.Notes, n => n.Contains("deleted 1 old file"));
        Assert.Contains(result.Notes, n => n.Contains("still needed by newer differential/log"));
    }
}
