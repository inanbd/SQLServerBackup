using SqlBackup.Core.Models;

namespace SqlBackup.Core.Backup;

/// <summary>Resolves which databases a job targets, including run-time discovery modes.</summary>
public static class DatabaseSelector
{
    public static readonly string[] SystemDatabases = { "master", "model", "msdb" };

    /// <summary>
    /// Resolves the target list. <paramref name="databasesOnServer"/> is required for the
    /// discovery modes and ignored for <see cref="DatabaseSelectionMode.Explicit"/>.
    /// </summary>
    public static List<string> Resolve(
        DatabaseSelectionMode mode,
        IReadOnlyList<string> explicitDatabases,
        IReadOnlyList<string> excludedDatabases,
        IReadOnlyList<string>? databasesOnServer)
    {
        if (mode == DatabaseSelectionMode.Explicit)
            return explicitDatabases.ToList();

        ArgumentNullException.ThrowIfNull(databasesOnServer);
        var excluded = new HashSet<string>(excludedDatabases, StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> candidates = databasesOnServer;
        if (mode == DatabaseSelectionMode.AllUserDatabases)
            candidates = candidates.Where(db => !SystemDatabases.Contains(db, StringComparer.OrdinalIgnoreCase));
        return candidates.Where(db => !excluded.Contains(db)).ToList();
    }

    public static string Describe(BackupJob job)
    {
        var exclusions = job.ExcludedDatabases.Count > 0
            ? $" (except {string.Join(", ", job.ExcludedDatabases)})"
            : "";
        return job.SelectionMode switch
        {
            DatabaseSelectionMode.AllUserDatabases => "All user databases" + exclusions,
            DatabaseSelectionMode.AllDatabases => "All databases" + exclusions,
            _ => string.Join(", ", job.Databases),
        };
    }
}
