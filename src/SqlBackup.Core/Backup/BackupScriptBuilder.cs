using System.Text;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Backup;

/// <summary>Generates the T-SQL for BACKUP DATABASE / BACKUP LOG / RESTORE VERIFYONLY.</summary>
public static class BackupScriptBuilder
{
    /// <summary>Bracket-quotes an identifier ([name]), escaping embedded ']'.</summary>
    public static string QuoteIdentifier(string name) => "[" + name.Replace("]", "]]") + "]";

    /// <summary>Produces an N'...' string literal, escaping embedded quotes.</summary>
    public static string QuoteNString(string value) => "N'" + value.Replace("'", "''") + "'";

    public static string BuildBackupCommand(
        string database,
        BackupType type,
        string filePath,
        BackupJobOptions options,
        DateTimeOffset timestamp)
    {
        var verb = type == BackupType.TransactionLog ? "LOG" : "DATABASE";

        var with = new List<string>();
        if (type == BackupType.Differential)
            with.Add("DIFFERENTIAL");
        // COPY_ONLY is valid for full and log backups; SQL Server rejects it for differentials.
        if (options.CopyOnly && type != BackupType.Differential)
            with.Add("COPY_ONLY");
        with.Add("INIT");
        with.Add("FORMAT");
        with.Add("SKIP");
        with.Add($"NAME = {QuoteNString(BuildBackupSetName(database, type, timestamp))}");
        if (options.Compression)
            with.Add("COMPRESSION");
        if (options.Checksum)
            with.Add("CHECKSUM");
        with.Add("STATS = 10");

        var sb = new StringBuilder();
        sb.Append("BACKUP ").Append(verb).Append(' ').Append(QuoteIdentifier(database));
        sb.Append(" TO DISK = ").Append(QuoteNString(filePath));
        sb.Append(" WITH ").Append(string.Join(", ", with));
        return sb.ToString();
    }

    public static string BuildVerifyCommand(string filePath, bool checksum) =>
        $"RESTORE VERIFYONLY FROM DISK = {QuoteNString(filePath)}" + (checksum ? " WITH CHECKSUM" : "");

    private static string BuildBackupSetName(string database, BackupType type, DateTimeOffset timestamp)
    {
        var name = $"{database} {type} {timestamp:yyyy-MM-dd HH:mm:ss}";
        // Media set names are limited to 128 characters.
        return name.Length <= 128 ? name : name[..128];
    }
}
