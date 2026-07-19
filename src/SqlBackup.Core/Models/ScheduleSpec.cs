namespace SqlBackup.Core.Models;

public sealed class ScheduleSpec
{
    public ScheduleKind Kind { get; set; } = ScheduleKind.Daily;

    /// <summary>Minutes between runs (<see cref="ScheduleKind.Interval"/> only).</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Local time of day for Daily/Weekly schedules.</summary>
    public TimeOnly TimeOfDay { get; set; } = new(2, 0);

    /// <summary>Weekdays for <see cref="ScheduleKind.Weekly"/> schedules.</summary>
    public List<DayOfWeek> Days { get; set; } = new() { DayOfWeek.Sunday };

    /// <summary>Cron expression for <see cref="ScheduleKind.Cron"/> (5 fields; 6 with seconds).</summary>
    public string CronExpression { get; set; } = "";

    public string Describe() => Kind switch
    {
        ScheduleKind.Interval when IntervalMinutes >= 60 && IntervalMinutes % 60 == 0 =>
            $"Every {IntervalMinutes / 60} hour(s)",
        ScheduleKind.Interval => $"Every {IntervalMinutes} minute(s)",
        ScheduleKind.Daily => $"Daily at {TimeOfDay:HH':'mm}",
        ScheduleKind.Weekly =>
            $"Weekly on {string.Join(", ", Days.Distinct().OrderBy(d => d))} at {TimeOfDay:HH':'mm}",
        ScheduleKind.Cron => $"Cron: {CronExpression}",
        _ => "Unknown schedule",
    };
}
