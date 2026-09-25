using System.Collections.Concurrent;

namespace EduCenter.Api.Automation;

/// <summary>Calendar dates as a branch sees them. Due dates and "is it overdue yet" are day-level business
/// facts: a payment due on the 4th is not late at 02:00 Tashkent time just because UTC is still on the 3rd.</summary>
public static class BranchClock
{
    private static readonly ConcurrentDictionary<string, TimeZoneInfo> Cache = new();

    public static DateOnly Today(string? timezoneId, DateTimeOffset now) => LocalDate(timezoneId, now);

    public static DateOnly LocalDate(string? timezoneId, DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Resolve(timezoneId)).DateTime);

    public static DateTime LocalDateTime(string? timezoneId, DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Resolve(timezoneId)).DateTime;

    /// <summary>The instant the branch's current calendar day began — the lower bound for "today" counters.</summary>
    public static DateTimeOffset StartOfLocalDay(string? timezoneId, DateTimeOffset now)
    {
        var zone = Resolve(timezoneId);
        var midnight = TimeZoneInfo.ConvertTime(now, zone).Date;
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }

    /// <summary>The instant a given calendar day began at this branch — the bounds of a report period as an instant range, so a
    /// query can cut on it in the database instead of converting every row.</summary>
    public static DateTimeOffset StartOfLocalDay(string? timezoneId, DateOnly date)
    {
        var zone = Resolve(timezoneId);
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }

    /// <summary>The instant at which the wall clock of this branch shows <paramref name="time"/> on <paramref name="date"/> — a weekly
    /// template's "Monday 18:00" turned into the instant a lesson is stored at.</summary>
    public static DateTimeOffset LocalToInstant(string? timezoneId, DateOnly date, TimeOnly time)
    {
        var zone = Resolve(timezoneId);
        var local = date.ToDateTime(time);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static TimeZoneInfo Resolve(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId)) return TimeZoneInfo.Utc;

        return Cache.GetOrAdd(timezoneId, id =>
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
            catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
        });
    }
}
