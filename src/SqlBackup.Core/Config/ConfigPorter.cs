using System.Text.Json;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Config;

/// <summary>
/// Config export/import for replicating a setup across machines. Exports strip
/// every protected secret: DPAPI blobs are machine-bound and would be dead
/// weight elsewhere — and decrypting them into a portable file would silently
/// turn an export into a credentials leak. Imports report which credentials
/// must be re-entered.
/// </summary>
public static class ConfigPorter
{
    public sealed record ImportResult(AppConfig Config, IReadOnlyList<string> Warnings);

    public static void ExportToFile(AppConfig config, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(CloneStripped(config), JsonDefaults.Pretty));

    internal static AppConfig CloneStripped(AppConfig config)
    {
        var clone = JsonSerializer.Deserialize<AppConfig>(
            JsonSerializer.Serialize(config, JsonDefaults.Compact), JsonDefaults.Compact)!;

        foreach (var connection in clone.Connections)
            connection.ProtectedPassword = null;
        clone.Notifications.ProtectedSmtpPassword = null;
        foreach (var destination in clone.OffsiteDestinations)
        {
            destination.ProtectedAzureSasToken = null;
            destination.ProtectedS3SecretKey = null;
            destination.ProtectedSftpPassword = null;
            destination.ProtectedSmbPassword = null;
            destination.ProtectedGoogleServiceAccountJson = null;
            destination.ProtectedGoogleClientSecret = null;
            destination.ProtectedGoogleRefreshToken = null;
        }
        return clone;
    }

    public static ImportResult LoadFromFile(string path)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonDefaults.Pretty)
                     ?? throw new InvalidDataException("The file contains no configuration.");

        var warnings = new List<string>();
        if (config.SchemaVersion > 1)
            warnings.Add($"The file was written by a newer version (schema {config.SchemaVersion}); unknown settings are ignored.");

        foreach (var connection in config.Connections.Where(c =>
                     c.AuthMode == SqlAuthMode.Sql && string.IsNullOrEmpty(c.ProtectedPassword)))
            warnings.Add($"Connection '{connection.Name}': re-enter the SQL login password.");

        if (config.Notifications.EmailEnabled &&
            !string.IsNullOrEmpty(config.Notifications.SmtpUsername) &&
            string.IsNullOrEmpty(config.Notifications.ProtectedSmtpPassword))
            warnings.Add("Notifications: re-enter the SMTP password.");

        foreach (var destination in config.OffsiteDestinations)
        {
            var missing = destination.Kind switch
            {
                OffsiteKind.AzureBlob => string.IsNullOrEmpty(destination.ProtectedAzureSasToken),
                OffsiteKind.S3 => string.IsNullOrEmpty(destination.ProtectedS3SecretKey),
                OffsiteKind.Sftp => string.IsNullOrEmpty(destination.ProtectedSftpPassword),
                // A share used under the engine's own identity has no secret to restore.
                OffsiteKind.SmbShare => !string.IsNullOrEmpty(destination.SmbUsername) &&
                                        string.IsNullOrEmpty(destination.ProtectedSmbPassword),
                OffsiteKind.GoogleDrive => destination.GoogleAuthMode == GoogleDriveAuthMode.ServiceAccount
                    ? string.IsNullOrEmpty(destination.ProtectedGoogleServiceAccountJson)
                    : string.IsNullOrEmpty(destination.ProtectedGoogleRefreshToken),
                _ => false,
            };
            if (missing)
            {
                warnings.Add(destination.Kind == OffsiteKind.GoogleDrive
                    ? $"Off-site destination '{destination.Name}': re-import the service account key, or authorize with Google again."
                    : $"Off-site destination '{destination.Name}': re-enter its secret.");
            }
        }

        return new ImportResult(config, warnings);
    }
}
