using SqlBackup.Core.Models;

namespace SqlBackup.Core.Monitoring;

public sealed record RpoBreach(Guid JobId, string JobName, string? Database, DateTimeOffset? LastSuccessUtc, int RpoHours)
{
    /// <summary>Stable identity for de-duplicating repeated alerts about the same breach.</summary>
    public string Key => $"{JobId}|{Database?.ToLowerInvariant()}";

    public string Describe()
    {
        var scope = Database is null ? $"Job '{JobName}'" : $"Job '{JobName}' / [{Database}]";
        var since = LastSuccessUtc is { } last
            ? $"last success {last.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "no successful backup recorded at all";
        return $"{scope}: target is a successful backup every {RpoHours}h — {since}.";
    }
}

/// <summary>
/// Missing-backup (RPO) detection: alerts when a job has produced no successful
/// backup within its RpoHours window — no matter why (service down, job disabled,
/// schedule broken, runs failing). Explicit jobs are checked per database;
/// discovery-mode jobs at job level (their database list only exists at run time).
/// </summary>
public static class RpoMonitor
{
    public static List<RpoBreach> Evaluate(
        IEnumerable<BackupJob> jobs,
        IReadOnlyDictionary<Guid, DateTimeOffset> lastSuccessPerJob,
        IReadOnlyDictionary<(Guid JobId, string Database), DateTimeOffset> lastSuccessPerDatabase,
        DateTimeOffset now)
    {
        var breaches = new List<RpoBreach>();
        foreach (var job in jobs.Where(j => j.RpoHours > 0))
        {
            var cutoff = now.AddHours(-job.RpoHours);

            if (job.SelectionMode == DatabaseSelectionMode.Explicit)
            {
                foreach (var database in job.Databases)
                {
                    lastSuccessPerDatabase.TryGetValue((job.Id, database.ToLowerInvariant()), out var last);
                    if (last == default || last < cutoff)
                        breaches.Add(new RpoBreach(job.Id, job.Name, database, last == default ? null : last, job.RpoHours));
                }
            }
            else
            {
                lastSuccessPerJob.TryGetValue(job.Id, out var last);
                if (last == default || last < cutoff)
                    breaches.Add(new RpoBreach(job.Id, job.Name, null, last == default ? null : last, job.RpoHours));
            }
        }
        return breaches;
    }
}
