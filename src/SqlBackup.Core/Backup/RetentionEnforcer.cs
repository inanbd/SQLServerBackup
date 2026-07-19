using SqlBackup.Core.Models;

namespace SqlBackup.Core.Backup;

public sealed record RetentionOutcome(IReadOnlyList<string> DeletedFiles, IReadOnlyList<string> Errors)
{
    public static readonly RetentionOutcome Empty = new(Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>
/// Deletes old backup files according to the job's retention policy. Only files
/// that match this tool's naming pattern for the same database and backup type
/// are considered, and the newest matching file is always kept.
/// </summary>
public static class RetentionEnforcer
{
    public static RetentionOutcome Apply(
        string folder,
        string database,
        BackupType type,
        RetentionPolicy policy,
        DateTime nowLocal)
    {
        if (policy.Mode == RetentionMode.KeepAll || !Directory.Exists(folder))
            return RetentionOutcome.Empty;

        var safeDb = BackupFileNamer.SanitizeForFileName(database);
        var typeToken = BackupFileNamer.TypeToken(type);

        var candidates = new List<(string Path, DateTime Timestamp)>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(path);
            if (!BackupFileNamer.TryParse(name, out var db, out var token, out var ts))
                continue;
            if (!db.Equals(safeDb, StringComparison.OrdinalIgnoreCase) ||
                !token.Equals(typeToken, StringComparison.OrdinalIgnoreCase))
                continue;
            candidates.Add((path, ts));
        }

        // Newest first; index 0 is always kept as a safety net.
        candidates.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

        IEnumerable<(string Path, DateTime Timestamp)> doomed = policy.Mode switch
        {
            RetentionMode.KeepLastN => candidates.Skip(Math.Max(1, policy.KeepLast)),
            RetentionMode.MaxAgeDays => candidates.Skip(1)
                .Where(c => c.Timestamp < nowLocal.AddDays(-Math.Max(0, policy.MaxAgeDays))),
            _ => Enumerable.Empty<(string, DateTime)>(),
        };

        var deleted = new List<string>();
        var errors = new List<string>();
        foreach (var (path, _) in doomed)
        {
            try
            {
                File.Delete(path);
                deleted.Add(path);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return new RetentionOutcome(deleted, errors);
    }
}
