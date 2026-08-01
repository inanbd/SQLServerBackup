using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace SqlBackup.Core.Offsite;

public sealed record GoogleAuthorizationResult(string RefreshToken, string? AccountEmail);

/// <summary>
/// One-time interactive Google authorization. Opens the system browser, runs a
/// loopback listener for the callback, and returns the refresh token to store.
/// Interactive by definition — call it from the desktop app, never the service.
/// </summary>
public static class GoogleDriveAuthorizer
{
    public static async Task<GoogleAuthorizationResult> AuthorizeAsync(
        string clientId, string clientSecret, CancellationToken ct = default)
    {
        var flow = GoogleDriveCredentials.CreateFlow(clientId, clientSecret);
        var credential = await new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver())
            .AuthorizeAsync("user", ct);

        var refreshToken = credential.Token.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
        {
            throw new InvalidOperationException(
                "Google did not return a refresh token. Remove this app's access at " +
                "https://myaccount.google.com/permissions and authorize again.");
        }

        string? email = null;
        try
        {
            using var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SqlBackup",
            });
            var request = service.About.Get();
            request.Fields = "user(emailAddress)";
            var about = await request.ExecuteAsync(ct);
            email = about.User?.EmailAddress;
        }
        catch
        {
            // The account label is cosmetic; authorization itself already succeeded.
        }

        return new GoogleAuthorizationResult(refreshToken, email);
    }
}
