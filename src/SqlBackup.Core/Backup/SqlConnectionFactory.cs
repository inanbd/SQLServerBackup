using Microsoft.Data.SqlClient;
using SqlBackup.Core.Models;
using SqlBackup.Core.Security;

namespace SqlBackup.Core.Backup;

public static class SqlConnectionFactory
{
    public static string BuildConnectionString(
        ConnectionProfile profile,
        ISecretProtector protector,
        string? database = null)
    {
        if (string.IsNullOrWhiteSpace(profile.Server))
            throw new InvalidOperationException($"Connection '{profile.Name}' has no server address.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server,
            InitialCatalog = database ?? "master",
            IntegratedSecurity = profile.AuthMode == SqlAuthMode.Windows,
            Encrypt = profile.Encrypt,
            TrustServerCertificate = profile.TrustServerCertificate,
            ConnectTimeout = Math.Max(1, profile.ConnectTimeoutSeconds),
            ApplicationName = "SqlBackup",
        };

        if (profile.AuthMode == SqlAuthMode.Sql)
        {
            builder.UserID = !string.IsNullOrWhiteSpace(profile.Username)
                ? profile.Username
                : throw new InvalidOperationException($"Connection '{profile.Name}': SQL authentication requires a user name.");
            builder.Password = profile.ProtectedPassword is { Length: > 0 } blob
                ? protector.Unprotect(blob)
                : throw new InvalidOperationException($"Connection '{profile.Name}': SQL authentication requires a stored password.");
        }

        return builder.ConnectionString;
    }
}
