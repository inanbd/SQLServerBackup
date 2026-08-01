namespace SqlBackup.Core.Models;

/// <summary>
/// A named off-site copy target. The populated fields depend on <see cref="Kind"/>;
/// secrets are DPAPI-protected like all other credentials in the config.
/// </summary>
public sealed class OffsiteDestination
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public OffsiteKind Kind { get; set; } = OffsiteKind.Sftp;

    /// <summary>Optional key/folder prefix applied to every upload (all kinds).</summary>
    public string Prefix { get; set; } = "";

    // --- AzureBlob ---
    /// <summary>Container URL, e.g. https://myaccount.blob.core.windows.net/backups</summary>
    public string? AzureContainerUrl { get; set; }
    /// <summary>DPAPI-protected SAS token for the container (needs create/write/list/delete rights).</summary>
    public string? ProtectedAzureSasToken { get; set; }

    // --- S3 ---
    public string? S3Bucket { get; set; }
    /// <summary>AWS region (e.g. eu-west-1). Ignored when <see cref="S3ServiceUrl"/> is set.</summary>
    public string? S3Region { get; set; }
    /// <summary>Custom endpoint for S3-compatible storage (MinIO, Backblaze B2, ...).</summary>
    public string? S3ServiceUrl { get; set; }
    public string? S3AccessKeyId { get; set; }
    public string? ProtectedS3SecretKey { get; set; }
    public bool S3ForcePathStyle { get; set; }

    // --- GoogleDrive ---
    public GoogleDriveAuthMode GoogleAuthMode { get; set; } = GoogleDriveAuthMode.OAuthUser;

    /// <summary>Drive folder ID that receives the backups (the long id from the folder URL).</summary>
    public string? GoogleFolderId { get; set; }

    /// <summary>DPAPI-protected service account key JSON.</summary>
    public string? ProtectedGoogleServiceAccountJson { get; set; }

    /// <summary>OAuth desktop client ID (from Google Cloud Console).</summary>
    public string? GoogleClientId { get; set; }

    public string? ProtectedGoogleClientSecret { get; set; }

    /// <summary>DPAPI-protected refresh token obtained by authorizing in the desktop app.</summary>
    public string? ProtectedGoogleRefreshToken { get; set; }

    /// <summary>Account that granted the refresh token, shown for reference only.</summary>
    public string? GoogleAuthorizedAccount { get; set; }

    // --- SmbShare ---
    /// <summary>UNC path, e.g. \\nas\backups\sql (a local path also works).</summary>
    public string? SmbPath { get; set; }
    /// <summary>Optional user for the share; empty means "use the identity the backup engine runs as".</summary>
    public string? SmbUsername { get; set; }
    public string? ProtectedSmbPassword { get; set; }

    // --- Sftp ---
    public string? SftpHost { get; set; }
    public int SftpPort { get; set; } = 22;
    public string? SftpUsername { get; set; }
    public string? ProtectedSftpPassword { get; set; }
    /// <summary>Remote base directory, e.g. /backups/sql</summary>
    public string? SftpRemotePath { get; set; }

    public string DescribeTarget() => Kind switch
    {
        OffsiteKind.AzureBlob => AzureContainerUrl ?? "",
        OffsiteKind.S3 => S3ServiceUrl is { Length: > 0 } url ? $"{url}/{S3Bucket}" : $"s3://{S3Bucket} ({S3Region})",
        OffsiteKind.Sftp => $"sftp://{SftpUsername}@{SftpHost}:{SftpPort}{SftpRemotePath}",
        OffsiteKind.SmbShare => SmbPath ?? "",
        OffsiteKind.GoogleDrive =>
            $"Google Drive folder {GoogleFolderId}" +
            (GoogleAuthMode == GoogleDriveAuthMode.ServiceAccount
                ? " (service account)"
                : GoogleAuthorizedAccount is { Length: > 0 } account ? $" ({account})" : " (user account)"),
        _ => "",
    };
}
