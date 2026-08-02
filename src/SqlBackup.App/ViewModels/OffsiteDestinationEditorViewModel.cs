using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using SqlBackup.App.Infrastructure;
using SqlBackup.Core.Models;
using SqlBackup.Core.Offsite;
using SqlBackup.Core.Security;

namespace SqlBackup.App.ViewModels;

public sealed class OffsiteDestinationEditorViewModel : ObservableObject
{
    private readonly ISecretProtector _protector;
    private string? _newSecret;
    private string? _newGoogleClientSecret;
    private OffsiteKind _kind;
    private GoogleDriveAuthMode _googleAuthMode;
    private string _testResultText = "";
    private string _googleAuthStatusText = "";

    public OffsiteDestinationEditorViewModel(OffsiteDestination working, ISecretProtector protector, bool isNew)
    {
        Working = working;
        _protector = protector;
        IsNew = isNew;
        _kind = working.Kind;
        _googleAuthMode = working.GoogleAuthMode;
        SftpPortText = working.SftpPort.ToString(CultureInfo.InvariantCulture);
        TestCommand = new AsyncRelayCommand(_ => TestAsync());
        AuthorizeGoogleCommand = new AsyncRelayCommand(_ => AuthorizeGoogleAsync());
        _googleAuthStatusText = DescribeStoredGoogleAuth();
    }

    public OffsiteDestination Working { get; }
    public bool IsNew { get; }
    public IReadOnlyList<OffsiteKind> Kinds { get; } = Enum.GetValues<OffsiteKind>();
    public IReadOnlyList<GoogleDriveAuthMode> GoogleAuthModes { get; } = Enum.GetValues<GoogleDriveAuthMode>();
    public ICommand TestCommand { get; }
    public ICommand AuthorizeGoogleCommand { get; }

    public string Name
    {
        get => Working.Name;
        set { Working.Name = value; OnPropertyChanged(); }
    }

    public OffsiteKind Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value))
            {
                Working.Kind = value;
                OnPropertyChanged(nameof(SecretLabel));
                OnPropertyChanged(nameof(SecretHint));
                OnPropertyChanged(nameof(SecretRequired));
                OnPropertyChanged(nameof(ShowSecretBox));
            }
        }
    }

    public string Prefix
    {
        get => Working.Prefix;
        set { Working.Prefix = value; OnPropertyChanged(); }
    }

    // Azure
    public string AzureContainerUrl
    {
        get => Working.AzureContainerUrl ?? "";
        set { Working.AzureContainerUrl = value; OnPropertyChanged(); }
    }

    // S3
    public string S3Bucket
    {
        get => Working.S3Bucket ?? "";
        set { Working.S3Bucket = value; OnPropertyChanged(); }
    }

    public string S3Region
    {
        get => Working.S3Region ?? "";
        set { Working.S3Region = value; OnPropertyChanged(); }
    }

    public string S3ServiceUrl
    {
        get => Working.S3ServiceUrl ?? "";
        set { Working.S3ServiceUrl = value; OnPropertyChanged(); }
    }

    public string S3AccessKeyId
    {
        get => Working.S3AccessKeyId ?? "";
        set { Working.S3AccessKeyId = value; OnPropertyChanged(); }
    }

    public bool S3ForcePathStyle
    {
        get => Working.S3ForcePathStyle;
        set { Working.S3ForcePathStyle = value; OnPropertyChanged(); }
    }

    // Google Drive
    public GoogleDriveAuthMode GoogleAuthMode
    {
        get => _googleAuthMode;
        set
        {
            if (Set(ref _googleAuthMode, value))
            {
                Working.GoogleAuthMode = value;
                OnPropertyChanged(nameof(IsGoogleServiceAccount));
                OnPropertyChanged(nameof(IsGoogleOAuth));
                GoogleAuthStatusText = DescribeStoredGoogleAuth();
            }
        }
    }

    public bool IsGoogleServiceAccount => GoogleAuthMode == GoogleDriveAuthMode.ServiceAccount;
    public bool IsGoogleOAuth => GoogleAuthMode == GoogleDriveAuthMode.OAuthUser;

    public string GoogleFolderId
    {
        get => Working.GoogleFolderId ?? "";
        set { Working.GoogleFolderId = value; OnPropertyChanged(); }
    }

    public string GoogleClientId
    {
        get => Working.GoogleClientId ?? "";
        set { Working.GoogleClientId = value.Length == 0 ? null : value; OnPropertyChanged(); }
    }

    public string GoogleAuthStatusText
    {
        get => _googleAuthStatusText;
        private set => Set(ref _googleAuthStatusText, value);
    }

    public void SetGoogleClientSecret(string secret) =>
        _newGoogleClientSecret = secret.Length == 0 ? null : secret;

    /// <summary>Reads a downloaded service account key file and stores it protected.</summary>
    public void LoadServiceAccountKey(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("client_email", out var email) ||
                !document.RootElement.TryGetProperty("private_key", out _))
            {
                GoogleAuthStatusText = "✗ That file is not a Google service account key (no client_email/private_key).";
                return;
            }
            Working.ProtectedGoogleServiceAccountJson = _protector.Protect(json);
            Working.GoogleAuthorizedAccount = email.GetString();
            GoogleAuthStatusText =
                $"✓ Service account key loaded for {email.GetString()}. Share the target folder with that address " +
                "(and remember service accounts can only own files in a Shared Drive).";
        }
        catch (Exception ex)
        {
            GoogleAuthStatusText = "✗ " + ex.Message;
        }
    }

    private string DescribeStoredGoogleAuth() => GoogleAuthMode switch
    {
        GoogleDriveAuthMode.ServiceAccount when Working.ProtectedGoogleServiceAccountJson is { Length: > 0 } =>
            $"A service account key is stored ({Working.GoogleAuthorizedAccount}).",
        GoogleDriveAuthMode.ServiceAccount => "No service account key loaded yet.",
        _ when Working.ProtectedGoogleRefreshToken is { Length: > 0 } =>
            $"Authorized as {Working.GoogleAuthorizedAccount ?? "a Google account"}.",
        _ => "Not authorized yet.",
    };

    private async Task AuthorizeGoogleAsync()
    {
        if (!GoogleDriveCredentials.TryValidateClientId(GoogleClientId, out var clientIdError))
        {
            GoogleAuthStatusText = "✗ " + clientIdError;
            return;
        }
        var clientSecret = _newGoogleClientSecret
                           ?? (Working.ProtectedGoogleClientSecret is { Length: > 0 } blob
                               ? _protector.Unprotect(blob)
                               : null);
        if (clientSecret is null)
        {
            GoogleAuthStatusText =
                "✗ Enter the OAuth client secret first — it is shown next to the client ID in Google Cloud Console " +
                "(usually starting with 'GOCSPX-'), and is not your Google account password.";
            return;
        }

        GoogleAuthStatusText = "A browser window has opened — approve access there…";
        try
        {
            var result = await GoogleDriveAuthorizer.AuthorizeAsync(GoogleClientId, clientSecret);
            Working.ProtectedGoogleClientSecret = _protector.Protect(clientSecret);
            Working.ProtectedGoogleRefreshToken = _protector.Protect(result.RefreshToken);
            Working.GoogleAuthorizedAccount = result.AccountEmail;
            _newGoogleClientSecret = null;
            GoogleAuthStatusText =
                $"✓ Authorized as {result.AccountEmail ?? "the selected account"}. The backup service will use this " +
                "authorization without needing a browser again.";
        }
        catch (Exception ex)
        {
            GoogleAuthStatusText = "✗ " + ex.Message;
        }
    }

    // SMB share
    public string SmbPath
    {
        get => Working.SmbPath ?? "";
        set { Working.SmbPath = value; OnPropertyChanged(); }
    }

    public string SmbUsername
    {
        get => Working.SmbUsername ?? "";
        set
        {
            Working.SmbUsername = value.Length == 0 ? null : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SecretRequired));
            OnPropertyChanged(nameof(SecretHint));
        }
    }

    // SFTP
    public string SftpHost
    {
        get => Working.SftpHost ?? "";
        set { Working.SftpHost = value; OnPropertyChanged(); }
    }

    public string SftpPortText { get; set; }

    public string SftpUsername
    {
        get => Working.SftpUsername ?? "";
        set { Working.SftpUsername = value; OnPropertyChanged(); }
    }

    public string SftpRemotePath
    {
        get => Working.SftpRemotePath ?? "";
        set { Working.SftpRemotePath = value; OnPropertyChanged(); }
    }

    public string SecretLabel => Kind switch
    {
        OffsiteKind.AzureBlob => "SAS token",
        OffsiteKind.S3 => "Secret access key",
        _ => "Password",
    };

    public bool HasStoredSecret => Kind switch
    {
        OffsiteKind.AzureBlob => Working.ProtectedAzureSasToken is { Length: > 0 },
        OffsiteKind.S3 => Working.ProtectedS3SecretKey is { Length: > 0 },
        OffsiteKind.SmbShare => Working.ProtectedSmbPassword is { Length: > 0 },
        _ => Working.ProtectedSftpPassword is { Length: > 0 },
    };

    /// <summary>
    /// Whether the shared secret box applies. A share accessed as the backup engine's
    /// own identity needs no secret, and Google Drive carries its own credential fields.
    /// </summary>
    public bool SecretRequired => Kind switch
    {
        OffsiteKind.SmbShare => !string.IsNullOrWhiteSpace(Working.SmbUsername),
        OffsiteKind.GoogleDrive => false,
        _ => true,
    };

    public bool ShowSecretBox => Kind != OffsiteKind.GoogleDrive;

    public string SecretHint =>
        HasStoredSecret ? "Leave blank to keep the stored secret."
        : !SecretRequired ? "Not needed: the share is accessed as the account the backup engine runs as."
        : "";

    public string TestResultText
    {
        get => _testResultText;
        private set => Set(ref _testResultText, value);
    }

    public void SetSecret(string secret) => _newSecret = secret.Length == 0 ? null : secret;

    public bool TryCommit(out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(Name))
        {
            error = "Enter a display name.";
            return false;
        }

        switch (Kind)
        {
            case OffsiteKind.AzureBlob when string.IsNullOrWhiteSpace(AzureContainerUrl):
                error = "Enter the container URL (https://account.blob.core.windows.net/container).";
                return false;
            case OffsiteKind.S3 when string.IsNullOrWhiteSpace(S3Bucket):
                error = "Enter the bucket name.";
                return false;
            case OffsiteKind.S3 when string.IsNullOrWhiteSpace(S3Region) && string.IsNullOrWhiteSpace(S3ServiceUrl):
                error = "Enter a region (or a custom endpoint URL for S3-compatible storage).";
                return false;
            case OffsiteKind.S3 when string.IsNullOrWhiteSpace(S3AccessKeyId):
                error = "Enter the access key id.";
                return false;
            case OffsiteKind.Sftp when string.IsNullOrWhiteSpace(SftpHost) || string.IsNullOrWhiteSpace(SftpUsername):
                error = "Enter the SFTP host and username.";
                return false;
            case OffsiteKind.SmbShare when string.IsNullOrWhiteSpace(SmbPath):
                error = @"Enter the share path (e.g. \\nas\backups\sql).";
                return false;
            case OffsiteKind.GoogleDrive when string.IsNullOrWhiteSpace(GoogleFolderId):
                error = "Enter the Drive folder ID that should receive the backups.";
                return false;
            case OffsiteKind.GoogleDrive when IsGoogleServiceAccount &&
                                              string.IsNullOrEmpty(Working.ProtectedGoogleServiceAccountJson):
                error = "Load the service account key file.";
                return false;
            case OffsiteKind.GoogleDrive when IsGoogleOAuth &&
                                              !GoogleDriveCredentials.TryValidateClientId(GoogleClientId, out var clientIdError):
                error = clientIdError;
                return false;
            case OffsiteKind.GoogleDrive when IsGoogleOAuth &&
                                              string.IsNullOrEmpty(Working.ProtectedGoogleRefreshToken):
                error = "Authorize with Google first — the backup service cannot show a consent screen itself.";
                return false;
        }

        if (Kind == OffsiteKind.Sftp)
        {
            if (!int.TryParse(SftpPortText, out var port) || port is < 1 or > 65535)
            {
                error = "SFTP port must be a number between 1 and 65535.";
                return false;
            }
            Working.SftpPort = port;
        }

        if (SecretRequired && _newSecret is null && !HasStoredSecret)
        {
            error = $"Enter the {SecretLabel.ToLowerInvariant()}.";
            return false;
        }
        if (_newSecret is not null)
            StoreSecret(_protector.Protect(_newSecret));
        else if (!SecretRequired)
            StoreSecretRaw(null); // share used as the engine identity: drop any stale secret

        return true;
    }

    private void StoreSecret(string protectedValue) => StoreSecretRaw(protectedValue);

    private void StoreSecretRaw(string? protectedValue)
    {
        switch (Kind)
        {
            case OffsiteKind.AzureBlob:
                Working.ProtectedAzureSasToken = protectedValue;
                break;
            case OffsiteKind.S3:
                Working.ProtectedS3SecretKey = protectedValue;
                break;
            case OffsiteKind.SmbShare:
                Working.ProtectedSmbPassword = protectedValue;
                break;
            default:
                Working.ProtectedSftpPassword = protectedValue;
                break;
        }
    }

    private async Task TestAsync()
    {
        TestResultText = "Testing…";
        try
        {
            var probe = Cloner.DeepClone(Working);
            probe.Kind = Kind;
            probe.GoogleAuthMode = GoogleAuthMode;
            if (int.TryParse(SftpPortText, out var port))
                probe.SftpPort = port;
            if (_newSecret is not null)
            {
                var protectedValue = _protector.Protect(_newSecret);
                switch (Kind)
                {
                    case OffsiteKind.AzureBlob: probe.ProtectedAzureSasToken = protectedValue; break;
                    case OffsiteKind.S3: probe.ProtectedS3SecretKey = protectedValue; break;
                    case OffsiteKind.SmbShare: probe.ProtectedSmbPassword = protectedValue; break;
                    default: probe.ProtectedSftpPassword = protectedValue; break;
                }
            }

            using var provider = OffsiteProviderFactory.Create(probe, _protector);
            await provider.TestAsync();
            TestResultText = $"✓ Connected to {provider.Describe()}";
        }
        catch (Exception ex)
        {
            TestResultText = "✗ " + ex.Message;
        }
    }
}
