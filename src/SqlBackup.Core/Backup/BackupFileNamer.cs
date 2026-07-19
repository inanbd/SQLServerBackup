using System.Globalization;
using System.Text.RegularExpressions;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Backup;

/// <summary>
/// Backup file naming: {Database}_{Full|Diff|Log}_{yyyyMMdd_HHmmss}.bak/.trn.
/// The pattern is also what the retention enforcer uses to recognize files this
/// tool created — it never deletes anything that does not match.
/// </summary>
public static partial class BackupFileNamer
{
    [GeneratedRegex(@"^(?<db>.+)_(?<type>Full|Diff|Log)_(?<ts>\d{8}_\d{6})\.(?:bak|trn)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNameRegex();

    private const string TimestampFormat = "yyyyMMdd_HHmmss";

    // The Windows-invalid set, applied on every platform so names stay deterministic
    // (backups usually land on NTFS/UNC targets even when tooling runs elsewhere).
    private static readonly char[] InvalidFileNameChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    public static string SanitizeForFileName(string database)
    {
        var chars = database.Trim()
            .Select(c => c < 0x20 || InvalidFileNameChars.Contains(c) ? '_' : c)
            .ToArray();
        var result = new string(chars);
        return result.Length == 0 ? "_" : result;
    }

    public static string TypeToken(BackupType type) => type switch
    {
        BackupType.Full => "Full",
        BackupType.Differential => "Diff",
        BackupType.TransactionLog => "Log",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static string Extension(BackupType type) =>
        type == BackupType.TransactionLog ? ".trn" : ".bak";

    public static string BuildFileName(string database, BackupType type, DateTime localTimestamp) =>
        $"{SanitizeForFileName(database)}_{TypeToken(type)}_{localTimestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)}{Extension(type)}";

    public static bool TryParse(string fileName, out string database, out string typeToken, out DateTime timestamp)
    {
        database = "";
        typeToken = "";
        timestamp = default;

        var match = FileNameRegex().Match(fileName);
        if (!match.Success)
            return false;
        if (!DateTime.TryParseExact(match.Groups["ts"].Value, TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp))
            return false;

        database = match.Groups["db"].Value;
        typeToken = match.Groups["type"].Value;
        return true;
    }
}
