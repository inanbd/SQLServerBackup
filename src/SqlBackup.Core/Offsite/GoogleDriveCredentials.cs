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
