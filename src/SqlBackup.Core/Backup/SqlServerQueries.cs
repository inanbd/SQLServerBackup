using Microsoft.Data.SqlClient;

namespace SqlBackup.Core.Backup;

/// <summary>Small metadata queries used for validation and chain awareness.</summary>
public static class SqlServerQueries
{
    public sealed record ServerInfo(string ProductVersion, string Edition, string ServerName);

    public static async Task<ServerInfo> TestConnectionAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            """
            SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)),
                   CAST(SERVERPROPERTY('Edition') AS nvarchar(128)),
                   CAST(SERVERPROPERTY('ServerName') AS nvarchar(128))
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new ServerInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    public static async Task<List<string>> ListDatabasesAsync(string connectionString, CancellationToken ct = default)
    {
        var result = new List<string>();
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT name FROM sys.databases WHERE name <> 'tempdb' AND state_desc = 'ONLINE' ORDER BY name", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }

    public static async Task<string?> GetRecoveryModelAsync(string connectionString, string database, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT recovery_model_desc FROM sys.databases WHERE name = @name", conn);
        cmd.Parameters.AddWithValue("@name", database);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>True when msdb records at least one full (non-snapshot) backup for the database.</summary>
    public static async Task<bool> HasFullBackupAsync(string connectionString, string database, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM msdb.dbo.backupset WHERE database_name = @name AND type = 'D' AND is_snapshot = 0", conn);
        cmd.Parameters.AddWithValue("@name", database);
        var count = (int)(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return count > 0;
    }

    /// <summary>Rough upper bound for the backup size: total allocated file size of the database.</summary>
    public static async Task<long?> GetDatabaseSizeBytesAsync(string connectionString, string database, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT SUM(CAST(size AS bigint)) * 8192 FROM sys.master_files WHERE database_id = DB_ID(@name)", conn);
        cmd.Parameters.AddWithValue("@name", database);
        var value = await cmd.ExecuteScalarAsync(ct);
        return value is long bytes ? bytes : null;
    }
}
