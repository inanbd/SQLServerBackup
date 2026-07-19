using SqlBackup.Core.Models;
using SqlBackup.Core.Reporting;
using Xunit;

namespace SqlBackup.Core.Tests;

public class ReportBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    private static JobHistoryEntry Entry(
        string job, string db, BackupType type, int daysAgo, bool success = true, long? size = null) => new()
    {
        JobName = job,
        Database = db,
        Type = type,
        StartedUtc = Now.AddDays(-daysAgo),
        Success = success,
        FileSizeBytes = size,
        DurationSeconds = 10,
    };

    [Fact]
    public void Summary_CountsRunsFailuresAndBytes()
    {
        var entries = new List<JobHistoryEntry>
        {
            Entry("j", "Db", BackupType.Full, 2, success: true, size: 100),
            Entry("j", "Db", BackupType.Full, 1, success: false),
            Entry("j", "Db", BackupType.Full, 40, success: true, size: 999), // outside 30d window
        };

        var model = ReportBuilder.Build(entries, Now);

        Assert.Equal(2, model.Summary.TotalRuns);
        Assert.Equal(1, model.Summary.Failures);
        Assert.Equal(50, model.Summary.SuccessRatePercent, precision: 1);
        Assert.Equal(100, model.Summary.BytesWritten);
    }

    [Fact]
    public void Rows_TrackGrowthAndLastFull()
    {
        var entries = new List<JobHistoryEntry>
        {
            Entry("j", "Db", BackupType.Full, 20, size: 1000),
            Entry("j", "Db", BackupType.Full, 10, size: 1100),
            Entry("j", "Db", BackupType.Full, 1, size: 1200),
        };

        var model = ReportBuilder.Build(entries, Now);

        var row = Assert.Single(model.Rows);
        Assert.Equal(1200, row.LastFullSizeBytes);
        Assert.Equal(20, row.GrowthPercent!.Value, precision: 1);
        Assert.Equal(100, row.SuccessRatePercent);
        Assert.Equal(ReportBuilder.TrendDays, row.DailyFullSizesMb.Count);
        // The 1-day-old full lands near the end of the trend axis with a non-zero bar.
        Assert.True(row.DailyFullSizesMb[^2] > 0 || row.DailyFullSizesMb[^1] > 0);
    }

    [Fact]
    public void OffsiteFailures_CountAsFailures()
    {
        var entry = Entry("j", "Db", BackupType.Full, 1, success: true, size: 10);
        entry.OffsiteSuccess = false;

        var model = ReportBuilder.Build(new List<JobHistoryEntry> { entry }, Now);

        Assert.Equal(1, model.Summary.Failures);
        Assert.Equal(0, Assert.Single(model.Rows).SuccessRatePercent);
    }

    [Fact]
    public void DatabasesAreGrouped_CaseInsensitively_PerJob()
    {
        var entries = new List<JobHistoryEntry>
        {
            Entry("j1", "sales", BackupType.Full, 2, size: 10),
            Entry("j1", "Sales", BackupType.Full, 1, size: 10),
            Entry("j2", "Sales", BackupType.Full, 1, size: 10),
        };

        var model = ReportBuilder.Build(entries, Now);

        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(2, model.Rows.Single(r => r.JobName == "j1").RunCount);
    }

    [Fact]
    public void EmptyHistory_ProducesEmptyButValidModel()
    {
        var model = ReportBuilder.Build(Array.Empty<JobHistoryEntry>(), Now);

        Assert.Empty(model.Rows);
        Assert.Equal(100, model.Summary.SuccessRatePercent);
    }
}
