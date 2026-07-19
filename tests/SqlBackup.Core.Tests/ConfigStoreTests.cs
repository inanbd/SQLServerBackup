using SqlBackup.Core.Config;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void MissingFile_LoadsDefaults()
    {
        using var dir = new TempDirectory();
        var store = new ConfigStore(Path.Combine(dir.Path, "config.json"));

        var config = store.Load();

        Assert.Empty(config.Connections);
        Assert.Empty(config.Jobs);
        Assert.Equal(1, config.SchemaVersion);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAllModelDetails()
    {
        using var dir = new TempDirectory();
        var store = new ConfigStore(Path.Combine(dir.Path, "config.json"));

        var connection = new ConnectionProfile
        {
            Name = "prod",
            Server = @"db01\SQLEXPRESS,1433",
            AuthMode = SqlAuthMode.Sql,
            Username = "backup_user",
            ProtectedPassword = "dpapi:AAAA",
        };
        var job = new BackupJob
        {
            Name = "Nightly",
            ConnectionId = connection.Id,
            Databases = { "Sales", "Inventory" },
            Type = BackupType.Differential,
            Schedule = new ScheduleSpec
            {
                Kind = ScheduleKind.Weekly,
                TimeOfDay = new TimeOnly(2, 45),
                // Assign, don't collection-init: the initializer syntax would append
                // to the default list (which already contains Sunday).
                Days = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            },
            DestinationFolder = @"\\nas\backups",
            Retention = new RetentionPolicy { Mode = RetentionMode.MaxAgeDays, MaxAgeDays = 21 },
            Options = new BackupJobOptions { Compression = true, CopyOnly = true },
            CatchUpMissedRun = true,
        };
        store.Save(new AppConfig { Connections = { connection }, Jobs = { job } });

        var loaded = store.Load();

        var loadedConn = Assert.Single(loaded.Connections);
        Assert.Equal(connection.Id, loadedConn.Id);
        Assert.Equal(SqlAuthMode.Sql, loadedConn.AuthMode);
        Assert.Equal(@"db01\SQLEXPRESS,1433", loadedConn.Server);

        var loadedJob = Assert.Single(loaded.Jobs);
        Assert.Equal(BackupType.Differential, loadedJob.Type);
        Assert.Equal(new TimeOnly(2, 45), loadedJob.Schedule.TimeOfDay);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Friday }, loadedJob.Schedule.Days);
        Assert.Equal(RetentionMode.MaxAgeDays, loadedJob.Retention.Mode);
        Assert.True(loadedJob.Options.Compression);
        Assert.True(loadedJob.CatchUpMissedRun);
        Assert.True(loaded.ModifiedUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public void Update_AppliesMutationAtomically()
    {
        using var dir = new TempDirectory();
        var store = new ConfigStore(Path.Combine(dir.Path, "config.json"));
        store.Save(new AppConfig());

        store.Update(c => c.Connections.Add(new ConnectionProfile { Name = "added" }));

        Assert.Equal("added", Assert.Single(store.Load().Connections).Name);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }
}
