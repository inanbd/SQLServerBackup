namespace SqlBackup.Core.Models;

/// <summary>A saved SQL Server instance connection.</summary>
public sealed class ConnectionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Server address: host, host\instance, or host,port.</summary>
    public string Server { get; set; } = "";

    public SqlAuthMode AuthMode { get; set; } = SqlAuthMode.Windows;

    /// <summary>SQL login name (SQL authentication only).</summary>
    public string? Username { get; set; }

    /// <summary>
    /// DPAPI-protected password blob (SQL authentication only). Never plaintext;
    /// see <see cref="Security.SecretProtector"/> for the storage format.
    /// </summary>
    public string? ProtectedPassword { get; set; }

    /// <summary>Whether to require TLS encryption for the SQL connection.</summary>
    public bool Encrypt { get; set; }

    public bool TrustServerCertificate { get; set; } = true;

    public int ConnectTimeoutSeconds { get; set; } = 15;
}
