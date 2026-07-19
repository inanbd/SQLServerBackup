using SqlBackup.Core.Models;

namespace SqlBackup.Core.Reporting;

public sealed class ReportSummary
{
    public int WindowDays { get; init; }
    public int TotalRuns { get; init; }
    public int Failures { get; init; }
    public double SuccessRatePercent { get; init; }
    public long BytesWritten { get; init; }
    /// <summary>Sum of the newest full backup per database — a proxy for "current backup footprint".</summary>
    public long LatestFullTotalBytes { get; init; }
}

public sealed class DatabaseReportRow
{
    public required string JobName { get; init; }
    public required string Database { get; init; }
    public DateTimeOffset? LastSuccessUtc { get; init; }
    public DateTimeOffset? LastFullUtc { get; init; }
    public long? LastFullSizeBytes { get; init; }
    public int RunCount { get; init; }
    public double SuccessRatePercent { get; init; }
    public double AvgDurationSeconds { get; init; }
    public double MaxDurationSeconds { get; init; }
    /// <summary>Full-backup size change first→last inside the window, percent. Null with fewer than 2 fulls.</summary>
    public double? GrowthPercent { get; init; }
    /// <summary>Newest full-backup size (MB) per day for the trend chart, oldest day first.</summary>
    public IReadOnlyList<double> DailyFullSizesMb { get; init; } = Array.Empty<double>();
}

public sealed class ReportModel
{
    public required ReportSummary Summary { get; init; }
    public required IReadOnlyList<DatabaseReportRow> Rows { get; init; }
}

/// <summary>Aggregates job history into per-database trends. Pure — trivially testable.</summary>
public static class ReportBuilder
{
    public const int TrendDays = 14;

    public static ReportModel Build(IReadOnlyList<JobHistoryEntry> entries, DateTimeOffset now, int windowDays = 30)
    {
        var cutoff = now.AddDays(-windowDays);
        var window = entries.Where(e => e.StartedUtc >= cutoff).ToList();

        var rows = new List<DatabaseReportRow>();
        foreach (var group in window
                     .GroupBy(e => (e.JobName, Database: e.Database.ToLowerInvariant()))
                     .OrderBy(g => g.Key.JobName).ThenBy(g => g.Key.Database))
        {
            var items = group.OrderBy(e => e.StartedUtc).ToList();
            var successes = items.Where(e => e.IsFullySuccessful).ToList();
            var fulls = successes.Where(e => e.Type == BackupType.Full && e.FileSizeBytes is > 0).ToList();
            var lastFull = fulls.LastOrDefault();

            double? growth = null;
            if (fulls.Count >= 2 && fulls[0].FileSizeBytes is > 0)
                growth = (fulls[^1].FileSizeBytes!.Value - fulls[0].FileSizeBytes!.Value) * 100.0 / fulls[0].FileSizeBytes!.Value;

            var daily = new double[TrendDays];
            foreach (var full in fulls)
            {
                var age = (int)(now.Date - full.StartedUtc.ToLocalTime().Date).TotalDays;
                if (age is >= 0 and < TrendDays)
                {
                    var index = TrendDays - 1 - age;
                    daily[index] = Math.Max(daily[index], full.FileSizeBytes!.Value / (1024.0 * 1024.0));
                }
            }

            rows.Add(new DatabaseReportRow
            {
                JobName = group.Key.JobName,
                Database = items[^1].Database, // original casing from the newest entry
                LastSuccessUtc = successes.Count > 0 ? successes[^1].StartedUtc : null,
                LastFullUtc = lastFull?.StartedUtc,
                LastFullSizeBytes = lastFull?.FileSizeBytes,
                RunCount = items.Count,
                SuccessRatePercent = items.Count == 0 ? 0 : successes.Count * 100.0 / items.Count,
                AvgDurationSeconds = items.Count == 0 ? 0 : items.Average(e => e.DurationSeconds),
                MaxDurationSeconds = items.Count == 0 ? 0 : items.Max(e => e.DurationSeconds),
                GrowthPercent = growth,
                DailyFullSizesMb = daily,
            });
        }

        var summary = new ReportSummary
        {
            WindowDays = windowDays,
            TotalRuns = window.Count,
            Failures = window.Count(e => !e.IsFullySuccessful),
            SuccessRatePercent = window.Count == 0 ? 100 : window.Count(e => e.IsFullySuccessful) * 100.0 / window.Count,
            BytesWritten = window.Where(e => e.Success).Sum(e => e.FileSizeBytes ?? 0),
            LatestFullTotalBytes = rows.Sum(r => r.LastFullSizeBytes ?? 0),
        };

        return new ReportModel { Summary = summary, Rows = rows };
    }
}
