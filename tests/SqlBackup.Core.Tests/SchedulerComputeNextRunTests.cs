using SqlBackup.Core.Models;
using SqlBackup.Service;
using Xunit;

namespace SqlBackup.Core.Tests;

public class SchedulerComputeNextRunTests
{
    private static BackupJob DailyJob(bool catchUp) => new()
    {
        Name = "daily",
        Enabled = true,
        CatchUpMissedRun = catchUp,
        Schedule = new ScheduleSpec { Kind = ScheduleKind.Daily, TimeOfDay = new TimeOnly(2, 0) },
    };

    [Fact]
    public void MissedOccurrence_WithCatchUp_IsDueImmediately()
    {
        var now = DateTimeOffset.UtcNow;
        var lastRun = now.AddDays(-3); // several occurrences missed

        var next = SchedulerWorker.ComputeNextRun(DailyJob(catchUp: true), lastRun, now, allowCatchUp: true, out var error);

        Assert.Null(error);
        Assert.NotNull(next);
        Assert.True(next < now, "a missed occurrence should surface as overdue (due now)");
    }

    [Fact]
    public void MissedOccurrence_WithoutCatchUp_WaitsForNextFutureOccurrence()
    {
        var now = DateTimeOffset.UtcNow;
        var lastRun = now.AddDays(-3);

        var next = SchedulerWorker.ComputeNextRun(DailyJob(catchUp: false), lastRun, now, allowCatchUp: true, out _);

        Assert.NotNull(next);
        Assert.True(next > now);
    }

    [Fact]
    public void ConfigReload_DoesNotReplayMissedRuns()
    {
        // allowCatchUp=false is used for mid-flight config reloads: even a catch-up job
        // must not fire because of an old lastRun timestamp.
        var now = DateTimeOffset.UtcNow;
        var next = SchedulerWorker.ComputeNextRun(DailyJob(catchUp: true), now.AddDays(-3), now, allowCatchUp: false, out _);

        Assert.True(next > now);
    }

    [Fact]
    public void InvalidCron_YieldsScheduleError()
    {
        var job = new BackupJob
        {
            Enabled = true,
            Schedule = new ScheduleSpec { Kind = ScheduleKind.Cron, CronExpression = "nonsense" },
        };

        var next = SchedulerWorker.ComputeNextRun(job, null, DateTimeOffset.UtcNow, true, out var error);

        Assert.Null(next);
        Assert.NotNull(error);
    }
}
