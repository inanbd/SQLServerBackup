using SqlBackup.Core.Models;

namespace SqlBackup.Core.Backup;

/// <summary>A backup file (local path or remote key) parsed from this tool's naming pattern.</summary>
public sealed record RetentionFile(string Key, string TypeToken, DateTime Timestamp);

public sealed record RetentionOutcome(
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<string> KeptForChain,
    IReadOnlyList<string> Errors)
{
    public static readonly RetentionOutcome Empty =
        new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>
/// Deletes old backup files according to the job's retention policy. Only files
/// matching this tool's naming pattern for the same database are considered, the
/// newest file of the job's type always survives, and — chain awareness — a full
/// backup is never deleted while surviving differential/log backups still depend
/// on it (i.e. lie between it and the next surviving full).
/// </summary>
public static class RetentionEnforcer
{
    /// <summary>
    /// Pure planning: given every pattern-matching file for one database (all types),
    /// decide what the policy deletes for <paramref name="jobType"/> and what must be
    /// spared to keep restore chains intact. Shared by local-disk and off-site retention.
    /// </summary>
    public static (List<RetentionFile> Delete, List<RetentionFile> KeepForChain) Plan(
        IReadOnlyList<RetentionFile> filesForDatabase,
        BackupType jobType,
        RetentionPolicy policy,
        DateTime nowLocal)
    {
        var keptForChain = new List<RetentionFile>();
        if (policy.Mode == RetentionMode.KeepAll)
            return (new List<RetentionFile>(), keptForChain);

        var typeToken = BackupFileNamer.TypeToken(jobType);
        var candidates = filesForDatabase
            .Where(f => f.TypeToken.Equals(typeToken, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Timestamp)
            .ToList();

        // Newest-first; index 0 is always kept as a safety net.
        var doomed = (policy.Mode switch
        {
            RetentionMode.KeepLastN => candidates.Skip(Math.Max(1, policy.KeepLast)),
            RetentionMode.MaxAgeDays => candidates.Skip(1)
                .Where(c => c.Timestamp < nowLocal.AddDays(-Math.Max(0, policy.MaxAgeDays))),
            _ => Enumerable.Empty<RetentionFile>(),
        }).ToList();

        if (jobType == BackupType.Full && doomed.Count > 0)
        {
            // A diff/log between full F and the next surviving full is based on F.
            var dependents = filesForDatabase
                .Where(f => !f.TypeToken.Equals(typeToken, StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Timestamp)
                .ToList();
            var survivingFulls = candidates.Except(doomed).Select(f => f.Timestamp).ToList();

            foreach (var full in doomed.OrderByDescending(f => f.Timestamp).ToList())
            {
                var nextSurviving = survivingFulls
                    .Where(ts => ts > full.Timestamp)
                    .DefaultIfEmpty(DateTime.MaxValue)
                    .Min();
                if (dependents.Any(ts => ts > full.Timestamp && ts < nextSurviving))
                {
                    doomed.Remove(full);
                    keptForChain.Add(full);
                    survivingFulls.Add(full.Timestamp);
                }
            }
        }

        return (doomed, keptForChain);
    }

    /// <summary>Applies the plan to a local folder.</summary>
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
        var files = new List<RetentionFile>();
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (BackupFileNamer.TryParse(Path.GetFileName(path), out var db, out var token, out var ts) &&
                db.Equals(safeDb, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(new RetentionFile(path, token, ts));
            }
        }

        var (doomed, keptForChain) = Plan(files, type, policy, nowLocal);

        var deleted = new List<string>();
        var errors = new List<string>();
        foreach (var file in doomed)
        {
            try
            {
                File.Delete(file.Key);
                deleted.Add(file.Key);
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file.Key)}: {ex.Message}");
            }
        }

        return new RetentionOutcome(deleted, keptForChain.Select(f => f.Key).ToList(), errors);
    }
}
