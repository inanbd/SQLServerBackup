using SqlBackup.Core.Config;
using SqlBackup.Core.Models;
using Xunit;

namespace SqlBackup.Core.Tests;

public class ConfigPorterTests
{
    private static AppConfig SampleConfig() => new()
    {
        Connections =
        {
            new ConnectionProfile
            {
                Name = "prod",
                AuthMode = SqlAuthMode.Sql,
                Username = "u",
                ProtectedPassword = "dpapi:AAAA",
            },
        },
        Jobs = { new BackupJob { Name = "nightly" } },
        OffsiteDestinations =
        {
            new OffsiteDestination { Name = "nas", Kind = OffsiteKind.Sftp, SftpHost = "h", SftpUsername = "u", ProtectedSftpPassword = "dpapi:BBBB" },
        },
        Notifications = new NotificationSettings { EmailEnabled = true, SmtpUsername = "s", ProtectedSmtpPassword = "dpapi:CCCC" },
    };

    [Fact]
    public void Export_StripsEverySecret_ButKeepsStructure()
    {
        var stripped = ConfigPorter.CloneStripped(SampleConfig());

        Assert.Null(stripped.Connections[0].ProtectedPassword);
        Assert.Null(stripped.Notifications.ProtectedSmtpPassword);
        Assert.Null(stripped.OffsiteDestinations[0].ProtectedSftpPassword);
        Assert.Equal("prod", stripped.Connections[0].Name);
        Assert.Single(stripped.Jobs);
    }

    [Fact]
    public void ExportImport_RoundTrips_AndWarnsAboutMissingCredentials()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "export.json");
        ConfigPorter.ExportToFile(SampleConfig(), path);

        var result = ConfigPorter.LoadFromFile(path);

        Assert.Single(result.Config.Connections);
        Assert.Single(result.Config.Jobs);
        Assert.Single(result.Config.OffsiteDestinations);
        Assert.Contains(result.Warnings, w => w.Contains("prod"));
        Assert.Contains(result.Warnings, w => w.Contains("SMTP"));
        Assert.Contains(result.Warnings, w => w.Contains("nas"));
    }

    [Fact]
    public void Import_WithIntactSecrets_HasNoWarnings()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "config.json");
        new ConfigStore(path).Save(SampleConfig());

        var result = ConfigPorter.LoadFromFile(path);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Import_RejectsGarbage()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "bad.json");
        File.WriteAllText(path, "not json at all");

        Assert.ThrowsAny<Exception>(() => ConfigPorter.LoadFromFile(path));
    }
}
