using Microsoft.Extensions.Hosting;
using SqlBackup.Core;
using SqlBackup.Core.Config;
using SqlBackup.Core.Ipc;
using SqlBackup.Core.Models;
using SqlBackup.Service;
using Xunit;

namespace SqlBackup.Core.Tests;

/// <summary>
/// Boots the real service host (scheduler + IPC server) in-process against a
/// temp data directory and exercises the pipe exactly like the desktop app does.
/// No SQL Server is required: the run-job test points at an unreachable address
/// and asserts the failure surfaces in history.
/// </summary>
[Collection("service-e2e")]
public sealed class ServiceEndToEndTests : IAsyncLifetime
{
    private readonly TempDirectory _dataDir = new();
    private IHost? _host;
    private BackupJob _job = null!;

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnvVar, _dataDir.Path);
        Environment.SetEnvironmentVariable(IpcProtocol.PipeNameEnvVar, $"SqlBackupTest.{Guid.NewGuid():N}");

        var connection = new ConnectionProfile
        {
            Name = "unreachable",
            Server = "127.0.0.1,9", // discard port — connection refused instantly
            ConnectTimeoutSeconds = 1,
        };
        _job = new BackupJob
        {
            Name = "nightly",
            ConnectionId = connection.Id,
            Databases = { "master" },
            Schedule = new ScheduleSpec { Kind = ScheduleKind.Daily, TimeOfDay = new TimeOnly(2, 0) },
            DestinationFolder = Path.Combine(_dataDir.Path, "backups"),
            Options = new BackupJobOptions { VerifyAfterBackup = false },
        };
        var config = new AppConfig
        {
            Connections = { connection },
            Jobs = { _job },
            Service = new ServiceSettings { SqlConnectRetries = 0, SchedulerPollSeconds = 1 },
        };
        new ConfigStore().Save(config);

        _host = Program.BuildHost(Array.Empty<string>());
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(10));
            _host.Dispose();
        }
        Environment.SetEnvironmentVariable(AppPaths.DataDirEnvVar, null);
        Environment.SetEnvironmentVariable(IpcProtocol.PipeNameEnvVar, null);
        _dataDir.Dispose();
    }

    [Fact]
    public async Task FullIpcConversation_StatusRunJobHistoryReload()
    {
        var client = new IpcClient();

        // Ping
        var ping = await client.PingAsync(timeoutMs: 10_000);
        Assert.Equal(Environment.ProcessId, ping.ProcessId);

        // Status: our job is known, scheduled, not running.
        var status = await client.GetStatusAsync();
        Assert.False(string.IsNullOrEmpty(status.ServiceAccount));
        var jobStatus = Assert.Single(status.Jobs);
        Assert.Equal(_job.Id, jobStatus.JobId);
        Assert.Equal("nightly", jobStatus.Name);
        Assert.True(jobStatus.Enabled);
        Assert.NotNull(jobStatus.NextRunUtc);
        Assert.Null(jobStatus.ScheduleError);

        // Unknown job id is rejected.
        var rejected = await client.RunJobAsync(Guid.NewGuid());
        Assert.False(rejected.Accepted);

        // Real job id is accepted and the run fails fast (server unreachable),
        // landing in history with an error.
        var accepted = await client.RunJobAsync(_job.Id);
        Assert.True(accepted.Accepted, accepted.Reason);

        IReadOnlyList<JobHistoryEntry> entries = Array.Empty<JobHistoryEntry>();
        for (var i = 0; i < 60 && entries.Count == 0; i++)
        {
            await Task.Delay(500);
            entries = await client.GetHistoryAsync(10, _job.Id);
        }
        var entry = Assert.Single(entries);
        Assert.False(entry.Success);
        Assert.Equal(RunTrigger.Manual, entry.Trigger);
        Assert.Contains("unreachable", entry.Error, StringComparison.OrdinalIgnoreCase);

        // Status now reflects the failed run.
        var statusAfter = await client.GetStatusAsync();
        var jobAfter = Assert.Single(statusAfter.Jobs);
        Assert.False(jobAfter.IsRunning);
        Assert.False(jobAfter.LastRunSuccess);
        Assert.NotNull(jobAfter.NextRunUtc);

        // Config edit + reload-config: a new job appears without restarting the host.
        var store = new ConfigStore();
        store.Update(c => c.Jobs.Add(new BackupJob
        {
            Name = "second",
            ConnectionId = c.Connections[0].Id,
            Databases = { "model" },
            Schedule = new ScheduleSpec { Kind = ScheduleKind.Interval, IntervalMinutes = 999 },
            DestinationFolder = Path.Combine(_dataDir.Path, "backups2"),
        }));
        var reload = await client.ReloadConfigAsync();
        Assert.True(reload.ConfigModifiedUtc > DateTimeOffset.MinValue);

        var statusReloaded = await client.GetStatusAsync();
        Assert.Equal(2, statusReloaded.Jobs.Count);
        Assert.Contains(statusReloaded.Jobs, j => j.Name == "second");

        // Unknown message types produce a clean error, not a dropped connection.
        var unknown = await client.SendAsync("bogus-type");
        Assert.False(unknown.Ok);
        Assert.Contains("bogus-type", unknown.Error);
    }
}
