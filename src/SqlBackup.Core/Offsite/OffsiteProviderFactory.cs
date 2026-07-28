using SqlBackup.Core.Models;
using SqlBackup.Core.Security;

namespace SqlBackup.Core.Offsite;

public static class OffsiteProviderFactory
{
    /// <summary>Builds the provider for a destination, decrypting its secret. Throws on incomplete config.</summary>
    public static IOffsiteProvider Create(OffsiteDestination destination, ISecretProtector protector)
    {
        switch (destination.Kind)
        {
            case OffsiteKind.AzureBlob:
                return new AzureBlobOffsiteProvider(
                    Require(destination.AzureContainerUrl, destination, "container URL"),
                    protector.Unprotect(Require(destination.ProtectedAzureSasToken, destination, "SAS token")));

            case OffsiteKind.S3:
                if (string.IsNullOrWhiteSpace(destination.S3ServiceUrl) && string.IsNullOrWhiteSpace(destination.S3Region))
                    throw Missing(destination, "region (or custom endpoint URL)");
                return new S3OffsiteProvider(
                    Require(destination.S3AccessKeyId, destination, "access key id"),
                    protector.Unprotect(Require(destination.ProtectedS3SecretKey, destination, "secret access key")),
                    Require(destination.S3Bucket, destination, "bucket"),
                    destination.S3Region,
                    destination.S3ServiceUrl,
                    destination.S3ForcePathStyle);

            case OffsiteKind.SmbShare:
                return new SmbOffsiteProvider(
                    Require(destination.SmbPath, destination, "share path"),
                    destination.SmbUsername,
                    // Credentials are optional: without them the share is accessed as
                    // the identity the backup engine runs as.
                    destination.ProtectedSmbPassword is { Length: > 0 } smbSecret
                        ? protector.Unprotect(smbSecret)
                        : null);

            case OffsiteKind.Sftp:
                return new SftpOffsiteProvider(
                    Require(destination.SftpHost, destination, "host"),
                    destination.SftpPort,
                    Require(destination.SftpUsername, destination, "username"),
                    protector.Unprotect(Require(destination.ProtectedSftpPassword, destination, "password")),
                    destination.SftpRemotePath ?? "/");

            default:
                throw new InvalidOperationException($"Unknown off-site destination kind '{destination.Kind}'.");
        }
    }

    private static string Require(string? value, OffsiteDestination destination, string what) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw Missing(destination, what);

    private static InvalidOperationException Missing(OffsiteDestination destination, string what) =>
        new($"Off-site destination '{destination.Name}' ({destination.Kind}) is missing its {what}.");
}
