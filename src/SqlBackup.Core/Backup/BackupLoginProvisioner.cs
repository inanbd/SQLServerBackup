using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace SqlBackup.Core.Backup;

public sealed record BackupLoginProvisionResult(
    string Username,
    string Password,
    bool ReusedExistingLogin,
    IReadOnlyList<string> GrantedDatabases,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Creates (or reuses) a dedicated SQL Server login for backups so jobs don't
/// depend on the Windows account the service runs as.
///
/// Naming: iSQLBackup_[first 5 chars of the connection title, letters/digits only]_[5 random digits].
/// If a login matching that prefix pattern already exists it is reused and its
/// password is reset (we can't know the old one) — repeated provisioning never
/// litters the server with logins.
///
/// Granted rights (least privilege for what this tool does):
///  - db_backupoperator in every online database (BACKUP DATABASE/LOG),
///  - db_datareader in msdb (backup-chain checks read msdb.dbo.backupset),
///  - CREATE ANY DATABASE at server level (SQL Server requires CREATE DATABASE
///    permission for RESTORE VERIFYONLY).
/// </summary>
public static class BackupLoginProvisioner
{
    public const string Prefix = "iSQLBackup_";

    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";
    // No quotes/backslashes/semicolons: the password must stay unremarkable in
    // T-SQL literals and connection strings even though both are escaped anyway.
    private const string Symbols = "!@#$%^&*()-_=+.?";

    /// <summary>"Production 01!" -> "iSQLBackup_Produ" (no random suffix yet).</summary>
    public static string BuildBaseName(string connectionTitle)
    {
        var cleaned = new string(connectionTitle.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (cleaned.Length == 0)
            cleaned = "Conn";
        return Prefix + cleaned[..Math.Min(5, cleaned.Length)];
    }

    public static string GenerateUserName(string connectionTitle) =>
        $"{BuildBaseName(connectionTitle)}_{RandomNumberGenerator.GetInt32(10_000, 100_000)}";

    public static string GeneratePassword(int length = 24)
    {
        length = Math.Max(12, length);
        var chars = new char[length];
        chars[0] = RandomNumberGenerator.GetItems<char>(Upper, 1)[0];
        chars[1] = RandomNumberGenerator.GetItems<char>(Lower, 1)[0];
        chars[2] = RandomNumberGenerator.GetItems<char>(Digits, 1)[0];
        chars[3] = RandomNumberGenerator.GetItems<char>(Symbols, 1)[0];
        var all = Upper + Lower + Digits + Symbols;
        RandomNumberGenerator.GetItems<char>(all, length - 4).CopyTo(chars.AsSpan(4));
        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    /// <summary>LIKE pattern (ESCAPE '\') matching any login previously provisioned for this title.</summary>
    internal static string BuildExistingLoginPattern(string baseName) =>
        baseName.Replace("_", @"\_") + @"\_[0-9][0-9][0-9][0-9][0-9]";

    /// <summary>Idempotent per-database grant batch: map the login + add backup (and optionally read) roles.</summary>
    internal static string BuildDatabaseGrantScript(string database, string login, bool includeHistoryRead)
    {
        var qdb = BackupScriptBuilder.QuoteIdentifier(database);
        var qlogin = BackupScriptBuilder.QuoteIdentifier(login);
        var nlogin = BackupScriptBuilder.QuoteNString(login);

        var sb = new StringBuilder();
        sb.AppendLine($"USE {qdb};");
        sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = {nlogin})");
        sb.AppendLine($"    CREATE USER {qlogin} FOR LOGIN {qlogin};");
        AppendRoleAdd(sb, "db_backupoperator", qlogin, nlogin);
        if (includeHistoryRead)
            AppendRoleAdd(sb, "db_datareader", qlogin, nlogin);
        return sb.ToString();
    }

    private static void AppendRoleAdd(StringBuilder sb, string role, string quotedLogin, string nstringLogin)
    {
        sb.AppendLine($"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_role_members rm
                           JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
                           JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
                           WHERE r.name = N'{role}' AND m.name = {nstringLogin})
                ALTER ROLE [{role}] ADD MEMBER {quotedLogin};
            """);
    }

    public static async Task<BackupLoginProvisionResult> ProvisionAsync(
        string adminConnectionString,
        string connectionTitle,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();

        await using var conn = new SqlConnection(adminConnectionString);
        await conn.OpenAsync(ct);

        // SQL logins are useless on a Windows-authentication-only server.
        await using (var modeCmd = new SqlCommand(
                         "SELECT CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int)", conn))
        {
            if (await modeCmd.ExecuteScalarAsync(ct) is int only && only == 1)
                throw new InvalidOperationException(
                    "This SQL Server allows Windows authentication only. Enable mixed-mode authentication " +
                    "(server Properties → Security → SQL Server and Windows Authentication mode, then restart " +
                    "SQL Server) before creating a SQL backup login.");
        }

        // Reuse a login provisioned earlier for this connection title, if any.
        var baseName = BuildBaseName(connectionTitle);
        string? existing;
        await using (var findCmd = new SqlCommand(
                         @"SELECT TOP 1 name FROM sys.server_principals
                           WHERE type = 'S' AND name LIKE @pattern ESCAPE '\' ORDER BY name", conn))
        {
            findCmd.Parameters.AddWithValue("@pattern", BuildExistingLoginPattern(baseName));
            existing = await findCmd.ExecuteScalarAsync(ct) as string;
        }

        var username = existing ?? GenerateUserName(connectionTitle);
        var password = GeneratePassword();
        var qlogin = BackupScriptBuilder.QuoteIdentifier(username);
        var qpassword = BackupScriptBuilder.QuoteNString(password);

        // CHECK_POLICY OFF: the generated password is far beyond any complexity policy,
        // and policy-driven lockout/expiration would silently kill scheduled backups.
        var loginScript = existing is null
            ? $"CREATE LOGIN {qlogin} WITH PASSWORD = {qpassword}, CHECK_POLICY = OFF"
            : $"ALTER LOGIN {qlogin} WITH PASSWORD = {qpassword}; ALTER LOGIN {qlogin} ENABLE;";
        await using (var loginCmd = new SqlCommand(loginScript, conn))
            await loginCmd.ExecuteNonQueryAsync(ct);

        try
        {
            await using var grantCmd = new SqlCommand($"GRANT CREATE ANY DATABASE TO {qlogin}", conn);
            await grantCmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException ex)
        {
            warnings.Add("Could not grant CREATE ANY DATABASE (needed for RESTORE VERIFYONLY): " + ex.Message);
        }

        var granted = new List<string>();
        foreach (var database in await SqlServerQueries.ListDatabasesAsync(adminConnectionString, ct))
        {
            try
            {
                var isMsdb = database.Equals("msdb", StringComparison.OrdinalIgnoreCase);
                await using var grantCmd = new SqlCommand(BuildDatabaseGrantScript(database, username, isMsdb), conn);
                await grantCmd.ExecuteNonQueryAsync(ct);
                granted.Add(database);
            }
            catch (SqlException ex)
            {
                warnings.Add($"{database}: {ex.Errors[0].Message}");
            }
        }

        // Prove the stored credential actually signs in before anyone relies on it.
        var probeBuilder = new SqlConnectionStringBuilder(adminConnectionString)
        {
            IntegratedSecurity = false,
            UserID = username,
            Password = password,
            InitialCatalog = "master",
        };
        await using (var probe = new SqlConnection(probeBuilder.ConnectionString))
            await probe.OpenAsync(ct);

        return new BackupLoginProvisionResult(username, password, existing is not null, granted, warnings);
    }
}
