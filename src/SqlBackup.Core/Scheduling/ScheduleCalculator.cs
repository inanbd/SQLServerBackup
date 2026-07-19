using Cronos;
using SqlBackup.Core.Models;

namespace SqlBackup.Core.Scheduling;

/// <summary>
/// Computes occurrences for all schedule kinds. Daily/Weekly are translated to
/// cron expressions and evaluated by Cronos (which handles DST correctly);
/// Interval schedules are anchored to the previous run.
/// </summary>
public static class ScheduleCalculator
{
    /// <summary>Translates Daily/Weekly specs to cron; passes Cron through. Interval is not cron-representable.</summary>
    public static string ToCronExpression(ScheduleSpec spec) => spec.Kind switch
    {
        ScheduleKind.Daily => $"{spec.TimeOfDay.Minute} {spec.TimeOfDay.Hour} * * *",
        ScheduleKind.Weekly => BuildWeeklyCron(spec),
        ScheduleKind.Cron => spec.CronExpression,
        _ => throw new InvalidOperationException($"{spec.Kind} schedules have no cron representation."),
    };

    private static string BuildWeeklyCron(ScheduleSpec spec)
    {
        if (spec.Days.Count == 0)
            throw new InvalidOperationException("Weekly schedule has no weekdays selected.");
        var days = string.Join(",", spec.Days.Distinct().OrderBy(d => d).Select(d => (int)d));
        return $"{spec.TimeOfDay.Minute} {spec.TimeOfDay.Hour} * * {days}";
    }

    public static CronExpression ParseCron(string expression)
    {
        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length == 6
            ? CronExpression.Parse(expression, CronFormat.IncludeSeconds)
            : CronExpression.Parse(expression);
    }

    /// <summary>
    /// Next occurrence strictly after <paramref name="after"/>. For Interval schedules the
    /// result is lastRun + interval (or <paramref name="after"/> itself when the job never ran),
    /// which may lie in the past — callers treat a past value as "due now".
    /// </summary>
    public static DateTimeOffset? GetNextOccurrence(
        ScheduleSpec spec,
        DateTimeOffset after,
        DateTimeOffset? lastRun,
        TimeZoneInfo timeZone)
    {
        if (spec.Kind == ScheduleKind.Interval)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(1, spec.IntervalMinutes));
            return lastRun is { } last ? last + interval : after;
        }

        return ParseCron(ToCronExpression(spec)).GetNextOccurrence(after, timeZone);
    }

    public static bool TryValidate(ScheduleSpec spec, out string error)
    {
        error = "";
        try
        {
            switch (spec.Kind)
            {
                case ScheduleKind.Interval when spec.IntervalMinutes < 1:
                    error = "Interval must be at least 1 minute.";
                    return false;
                case ScheduleKind.Interval:
                    return true;
                case ScheduleKind.Weekly when spec.Days.Count == 0:
                    error = "Select at least one weekday.";
                    return false;
                case ScheduleKind.Cron when string.IsNullOrWhiteSpace(spec.CronExpression):
                    error = "Cron expression is empty.";
                    return false;
                default:
                    ParseCron(ToCronExpression(spec));
                    return true;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Upcoming occurrences, for previewing a schedule in the UI.</summary>
    public static List<DateTimeOffset> PreviewNext(ScheduleSpec spec, int count, DateTimeOffset from, TimeZoneInfo timeZone)
    {
        var result = new List<DateTimeOffset>();
        var cursor = from;
        for (var i = 0; i < count; i++)
        {
            DateTimeOffset? next = spec.Kind == ScheduleKind.Interval
                ? cursor + TimeSpan.FromMinutes(Math.Max(1, spec.IntervalMinutes))
                : ParseCron(ToCronExpression(spec)).GetNextOccurrence(cursor, timeZone);
            if (next is null)
                break;
            result.Add(next.Value);
            cursor = next.Value;
        }
        return result;
    }
}
