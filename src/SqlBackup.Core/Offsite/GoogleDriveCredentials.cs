using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Util.Store;
using SqlBackup.Core.Models;
using SqlBackup.Core.Security;

namespace SqlBackup.Core.Offsite;

/// <summary>
/// Builds Drive credentials for both supported modes, and runs the one-time
/// interactive authorization used by the desktop app.
///
/// A Windows service can never show a consent screen, so the OAuth flow happens
/// once in the control panel; only the resulting refresh token is stored (DPAPI
/// protected) and the service exchanges it for access tokens on its own.
/// </summary>
public static class GoogleDriveCredentials
{
    /// <summary>
    /// Full Drive scope: the tool must read a folder it did not create (to enforce
    /// retention) and delete old backups in it, which the narrower drive.file scope
    /// does not allow for pre-existing folders.
    /// </summary>
    public static readonly string[] Scopes = { DriveService.Scope.Drive };

    public static ICredential Create(OffsiteDestination destination, ISecretProtector protector)
    {
        if (destination.GoogleAuthMode == GoogleDriveAuthMode.ServiceAccount)
        {
            var json = destination.ProtectedGoogleServiceAccountJson is { Length: > 0 } blob
                ? protector.Unprotect(blob)
                : throw new InvalidOperationException(
                    $"Google Drive destination '{destination.Name}' has no service account key.");
            return GoogleCredential.FromJson(json).CreateScoped(Scopes);
        }

        var refreshToken = destination.ProtectedGoogleRefreshToken is { Length: > 0 } tokenBlob
            ? protector.Unprotect(tokenBlob)
            : throw new InvalidOperationException(
                $"Google Drive destination '{destination.Name}' is not authorized yet — " +
                "use 'Authorize with Google' in the destination editor.");

        var flow = CreateFlow(
            destination.GoogleClientId ?? throw new InvalidOperationException(
                $"Google Drive destination '{destination.Name}' has no OAuth client ID."),
            destination.ProtectedGoogleClientSecret is { Length: > 0 } secretBlob
                ? protector.Unprotect(secretBlob)
                : throw new InvalidOperationException(
                    $"Google Drive destination '{destination.Name}' has no OAuth client secret."));

        return new UserCredential(flow, "user", new TokenResponse { RefreshToken = refreshToken });
    }

    public const string ClientIdSuffix = ".apps.googleusercontent.com";

    /// <summary>
    /// Checks the client ID's shape before starting the browser flow. Google answers a
    /// malformed client ID with an opaque "Access blocked / invalid_client" page, so
    /// catching it here is the difference between a fixable message and a dead end.
    /// </summary>
    public static bool TryValidateClientId(string? clientId, out string error)
    {
        error = "";
        var value = clientId?.Trim() ?? "";

        if (value.Length == 0)
        {
            error = "Enter the OAuth client ID from Google Cloud Console.";
            return false;
        }
        if (value.EndsWith(ClientIdSuffix, StringComparison.OrdinalIgnoreCase))
            return true;

        error = (value.Contains('@')
                    ? "That looks like an email address, not an OAuth client ID. "
                    : "That does not look like a Google OAuth client ID. ") +
                $"It is issued by Google and ends with '{ClientIdSuffix}', " +
                "for example 123456789012-abc123def456.apps.googleusercontent.com. " +
                "Get it from Google Cloud Console → APIs & Services → Credentials → " +
                "Create credentials → OAuth client ID → Application type: Desktop app.";
        return false;
    }

    public static GoogleAuthorizationCodeFlow CreateFlow(string clientId, string clientSecret) =>
        new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = Scopes,
            // Offline + forced consent: without both, Google returns a refresh token only
            // on the very first authorization of an account, and re-authorizing later
            // would silently yield a credential the service cannot renew.
            DataStore = new NullDataStore(),
            Prompt = "consent",
        });
}
