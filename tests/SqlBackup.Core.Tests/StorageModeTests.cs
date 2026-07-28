using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class StorageModeTests
{
    [Theory]
    [InlineData(BackupStorageMode.LocalAndOffsite, true, true)]
    [InlineData(BackupStorageMode.LocalAndOffsite, false, true)]
    [InlineData(BackupStorageMode.LocalOnly, true, true)]
    [InlineData(BackupStorageMode.LocalOnly, false, true)]
    [InlineData(BackupStorageMode.OffsiteOnly, true, false)]
    // The safety rule: off-site-only with no usable off-site target still keeps the
    // local file, so a run can never finish with zero copies.
    [InlineData(BackupStorageMode.OffsiteOnly, false, true)]
    public void KeepsLocalCopy_FollowsModeAndOffsiteAvailability(
        BackupStorageMode mode, bool offsiteAvailable, bool expected)
    {
        Assert.Equal(expected, BackupJob.KeepsLocalCopy(mode, offsiteAvailable));
    }

    [Fact]
    public void DefaultMode_KeepsBothCopies_SoOlderConfigsBehaveUnchanged()
    {
        var job = new BackupJob();

        Assert.Equal(BackupStorageMode.LocalAndOffsite, job.StorageMode);
        Assert.True(BackupJob.KeepsLocalCopy(job.StorageMode, offsiteAvailable: true));
    }

    [Theory]
    [InlineData(BackupStorageMode.LocalAndOffsite, "nas", "Local + off-site (nas)")]
    [InlineData(BackupStorageMode.OffsiteOnly, "nas", "Off-site only (nas)")]
    [InlineData(BackupStorageMode.LocalOnly, "nas", "Local only")]
    [InlineData(BackupStorageMode.LocalAndOffsite, null, "Local only")]
    [InlineData(BackupStorageMode.OffsiteOnly, null, "Local only")]
    public void DescribeStorage_ReadsTheEffectiveBehaviour(
        BackupStorageMode mode, string? offsiteName, string expected)
    {
        var job = new BackupJob { StorageMode = mode };

        Assert.Equal(expected, job.DescribeStorage(offsiteName));
    }
}
