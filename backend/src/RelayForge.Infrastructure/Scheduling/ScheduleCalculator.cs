using Cronos;
using RelayForge.Domain.Enums;

namespace RelayForge.Infrastructure.Scheduling;

/// <summary>
/// Pure schedule arithmetic: next fire times and which missed times to run. All times are
/// UTC; the cron is read in the schedule's own time zone, so "0 2 * * *" in Europe/London is
/// 02:00 local all year. Daylight saving (handled by Cronos given a TimeZoneInfo):
///   - spring forward: a time that doesn't exist that day (02:30 when clocks jump 02:00->03:00)
///     fires once, right after the jump;
///   - fall back: a time that happens twice (01:30 when clocks go 02:00->01:00) fires once,
///     at its first occurrence.
/// </summary>
public static class ScheduleCalculator
{
    /// <summary>A missed time later than this is "late" for the Skip policy.</summary>
    public static readonly TimeSpan OnTimeWindow = TimeSpan.FromMinutes(1);

    /// <summary>The most missed runs CatchUp fires at once; older ones are dropped.</summary>
    public const int CatchUpLimit = 24;

    /// <returns>An error message, or null if both are valid.</returns>
    public static string? Validate(string cron, string timeZone)
    {
        try
        {
            CronExpression.Parse(cron);
        }
        catch (CronFormatException ex)
        {
            return $"Invalid cron '{cron}': {ex.Message}";
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"Unknown time zone '{timeZone}'. Use an IANA name such as 'UTC' or 'Asia/Kolkata'.";
        }

        return null;
    }

    /// <summary>The first fire time strictly after <paramref name="after"/>, in UTC.</summary>
    public static DateTimeOffset Next(string cron, string timeZone, DateTimeOffset after) =>
        (CronExpression.Parse(cron).GetNextOccurrence(after, TimeZoneInfo.FindSystemTimeZoneById(timeZone))
         ?? throw new InvalidOperationException($"Cron '{cron}' never fires again.")).ToUniversalTime();

    /// <summary>
    /// Which scheduled times to run now, given the schedule was due at <paramref name="nextFireAt"/>
    /// and it is now <paramref name="now"/>; plus the next time to wake up.
    /// </summary>
    public static (IReadOnlyList<DateTimeOffset> Runs, DateTimeOffset NextFireAt) DueRuns(
        string cron,
        string timeZone,
        MisfirePolicy policy,
        DateTimeOffset nextFireAt,
        DateTimeOffset now)
    {
        var expression = CronExpression.Parse(cron);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
        var due = new List<DateTimeOffset>();
        DateTimeOffset? t = nextFireAt;

        // Walk the missed times, keeping only the most recent ones a policy could want.
        while (t is { } at && at <= now)
        {
            due.Add(at);

            if (due.Count > CatchUpLimit)
            {
                due.RemoveAt(0);
            }

            // Cronos answers in the zone's offset; everything stored is UTC.
            t = expression.GetNextOccurrence(at, zone)?.ToUniversalTime();
        }

        var next = t ?? throw new InvalidOperationException($"Cron '{cron}' never fires again.");

        IReadOnlyList<DateTimeOffset> runs = due.Count == 0 ? [] : policy switch
        {
            // Once, for the latest missed time.
            MisfirePolicy.FireOnce => [due[^1]],

            // Only a time that is due right now; anything late is dropped.
            MisfirePolicy.Skip => now - due[^1] <= OnTimeWindow ? [due[^1]] : [],

            // Every missed time, oldest first (capped).
            MisfirePolicy.CatchUp => due,

            _ => [],
        };

        return (runs, next);
    }
}
