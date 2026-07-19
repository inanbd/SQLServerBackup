using SqlBackup.Core.Models;
using SqlBackup.Core.Scheduling;
using Xunit;

namespace SqlBackup.Core.Tests;

public class ScheduleCalculatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Daily_TranslatesToCron_AndFindsNextOccurrence()
    {
        var spec = new ScheduleSpec { Kind = ScheduleKind.Daily, TimeOfDay = new TimeOnly(2, 30) };
        Assert.Equal("30 2 * * *", ScheduleCalculator.ToCronExpression(spec));

        var after = new DateTimeOffset(2026, 7, 18, 3, 0, 0, TimeSpan.Zero);
        var next = ScheduleCalculator.GetNextOccurrence(spec, after, lastRun: null, Utc);
        Assert.Equal(new DateTimeOffset(2026, 7, 19, 2, 30, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Weekly_TranslatesSelectedDays()
    {
        var spec = new ScheduleSpec
        {
            Kind = ScheduleKind.Weekly,
            TimeOfDay = new TimeOnly(23, 15),
            Days = new List<DayOfWeek> { DayOfWeek.Wednesday, DayOfWeek.Monday, DayOfWeek.Monday },
        };
        Assert.Equal("15 23 * * 1,3", ScheduleCalculator.ToCronExpression(spec));

        // 2026-07-18 is a Saturday; the next selected day is Monday the 20th.
        var after = new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);
        var next = ScheduleCalculator.GetNextOccurrence(spec, after, null, Utc);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 23, 15, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Interval_AnchorsToLastRun()
    {
        var spec = new ScheduleSpec { Kind = ScheduleKind.Interval, IntervalMinutes = 45 };
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        // Never ran: due immediately.
        Assert.Equal(now, ScheduleCalculator.GetNextOccurrence(spec, now, lastRun: null, Utc));

        // Ran 10 minutes ago: due in 35 minutes.
        var lastRun = now.AddMinutes(-10);
        Assert.Equal(lastRun.AddMinutes(45), ScheduleCalculator.GetNextOccurrence(spec, now, lastRun, Utc));

        // Ran 2 hours ago: result lies in the past, i.e. overdue -> due now.
        var staleRun = now.AddHours(-2);
        var next = ScheduleCalculator.GetNextOccurrence(spec, now, staleRun, Utc);
        Assert.True(next < now);
    }

    [Fact]
    public void CronKind_SupportsFiveAndSixFieldExpressions()
    {
        var five = new ScheduleSpec { Kind = ScheduleKind.Cron, CronExpression = "*/15 * * * *" };
        Assert.True(ScheduleCalculator.TryValidate(five, out _));

        var six = new ScheduleSpec { Kind = ScheduleKind.Cron, CronExpression = "30 */5 * * * *" };
        Assert.True(ScheduleCalculator.TryValidate(six, out _));
    }

    [Theory]
    [InlineData("not a cron")]
    [InlineData("99 99 * * *")]
    [InlineData("")]
    public void InvalidCron_FailsValidation(string expression)
    {
        var spec = new ScheduleSpec { Kind = ScheduleKind.Cron, CronExpression = expression };
        Assert.False(ScheduleCalculator.TryValidate(spec, out var error));
        Assert.NotEqual("", error);
    }

    [Fact]
    public void WeeklyWithNoDays_FailsValidation()
    {
        var spec = new ScheduleSpec { Kind = ScheduleKind.Weekly, Days = new List<DayOfWeek>() };
        Assert.False(ScheduleCalculator.TryValidate(spec, out _));
    }

    [Fact]
    public void PreviewNext_ReturnsRequestedCountInOrder()
    {
        var spec = new ScheduleSpec { Kind = ScheduleKind.Daily, TimeOfDay = new TimeOnly(1, 0) };
        var from = new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);

        var preview = ScheduleCalculator.PreviewNext(spec, 3, from, Utc);

        Assert.Equal(3, preview.Count);
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 1, 0, 0, TimeSpan.Zero), preview[0]);
        Assert.Equal(new DateTimeOffset(2026, 7, 19, 1, 0, 0, TimeSpan.Zero), preview[1]);
        Assert.Equal(new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero), preview[2]);
    }
}
