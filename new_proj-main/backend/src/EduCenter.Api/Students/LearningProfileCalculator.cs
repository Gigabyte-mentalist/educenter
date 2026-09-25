using EduCenter.Modules.Lms.Domain;

namespace EduCenter.Api.Students;

/// <summary>One graded result on some date — what the profile's averages and trends are made of.</summary>
public readonly record struct ProfileSample(DateTimeOffset At, decimal Value);

/// <param name="Value">An IELTS band; null while there is no result to average — no made-up number stands in for it.</param>
/// <param name="SampleSize">How many results the value rests on, so "2 results" and "5 results" are not read with the same trust.</param>
/// <param name="Trend">+1 rising, 0 steady, -1 falling; null when there are too few results to tell.</param>
public record SkillDto(decimal? Value, int SampleSize, int? Trend);

/// <param name="Value">A percentage 0–100; null while there is nothing to measure.</param>
/// <param name="SampleSize">Results averaged (Grammar, Vocabulary), lessons counted (Attendance) or homework given (Homework).</param>
public record PercentDto(decimal? Value, int SampleSize, int? Trend);

/// <summary>
/// The pure arithmetic behind the Student Learning Profile (docs/00_TZ.md §7): no queries, so the rules are testable one by one
/// and the same rules can serve the learning signals and the branch dashboard. Everything is computed from results the caller
/// hands in; nothing here reads the clock — "now" is an argument.
/// </summary>
public static class LearningProfileCalculator
{
    /// <summary>How many of the newest results an average looks at.</summary>
    public const int Window = 5;

    /// <summary>Weights for the newest result first: the latest counts five times what the fifth-latest does.</summary>
    private static readonly decimal[] Weights = [5, 4, 3, 2, 1];

    /// <summary>A change smaller than this many bands is "steady".</summary>
    public const decimal BandTrendThreshold = 0.25m;

    /// <summary>A change smaller than this many percentage points is "steady".</summary>
    public const decimal PercentTrendThreshold = 3m;

    /// <summary>A trend needs the newest results and the ones before them; this is the most it looks at from each side.</summary>
    private const int TrendGroup = 3;

    /// <summary>Attendance and homework are yes/no per lesson, so a trend over three of them would swing by a third each time:
    /// they compare five against five, and say nothing before there are six.</summary>
    private const int CountTrendGroup = 5;
    private const int CountTrendMinSamples = 6;

    // ---- the attendance rule, shared by the profile and the student overview ----

    /// <summary>An excused absence is neither for nor against the student — the same rule the group and teacher figures use.</summary>
    public static bool CountsForAttendance(AttendanceStatus status) => status != AttendanceStatus.Excused;

    /// <summary>Present, Late and Online all mean the student took part; only Absent does not.</summary>
    public static bool Attended(AttendanceStatus status) => status is AttendanceStatus.Present or AttendanceStatus.Late or AttendanceStatus.Online;

    /// <summary><paramref name="part"/> out of <paramref name="whole"/> as a percentage with one decimal; null when there is no whole.</summary>
    public static decimal? Rate(int part, int whole) => whole == 0 ? null : Math.Round(part * 100m / whole, 1);

    // ---- weighted average and trend ----

    /// <summary>Weighted mean of up to <see cref="Window"/> results, newest first — a recent result says more about a student
    /// today than an old one. Null when there are none.</summary>
    public static decimal? WeightedAverage(IReadOnlyList<decimal> newestFirst)
    {
        var used = Math.Min(newestFirst.Count, Window);
        if (used == 0) return null;

        decimal sum = 0, weight = 0;
        for (var i = 0; i < used; i++)
        {
            sum += newestFirst[i] * Weights[i];
            weight += Weights[i];
        }
        return sum / weight;
    }

    /// <summary>
    /// Whether the newest results are above (+1), below (−1) or level with (0) the ones before them, by more than
    /// <paramref name="threshold"/>. It compares the mean of the newest <paramref name="group"/> results with the mean of the
    /// <paramref name="group"/> before them; with fewer than two groups' worth of results the two sides are split as evenly as
    /// they can be (the newer side gets the odd one), so a student with three exams still shows a direction. Null under
    /// <paramref name="minSamples"/> (never below two): with one result there is nothing to compare.
    /// </summary>
    public static int? Trend(IReadOnlyList<decimal> newestFirst, decimal threshold, int group = TrendGroup, int minSamples = 2)
    {
        var count = newestFirst.Count;
        if (count < Math.Max(minSamples, 2)) return null;

        var recentCount = Math.Min(group, (count + 1) / 2);
        var previousCount = Math.Min(group, count - recentCount);
        var difference = newestFirst.Take(recentCount).Average() - newestFirst.Skip(recentCount).Take(previousCount).Average();
        return Math.Abs(difference) < threshold ? 0 : Math.Sign(difference);
    }

    // ---- the questions the learning signals ask ----

    /// <summary>The newest <paramref name="count"/> values, when there are that many and every one is below <paramref name="threshold"/>;
    /// otherwise null. A student with fewer results than the rule asks for is simply not judged yet.</summary>
    public static IReadOnlyList<decimal>? AllBelow(IReadOnlyList<decimal> newestFirst, int count, decimal threshold)
    {
        if (newestFirst.Count < count) return null;
        var latest = newestFirst.Take(count).ToList();
        return latest.All(value => value < threshold) ? latest : null;
    }

    /// <summary>The mean of the other sections of one exam and how far <paramref name="reading"/> trails it (positive: behind). Null with
    /// fewer than two other sections — one number is not a mean worth comparing a section against.</summary>
    public static (decimal OthersMean, decimal Gap)? ReadingGap(decimal reading, IReadOnlyCollection<decimal> others)
    {
        if (others.Count < 2) return null;
        var mean = others.Average();
        return (mean, mean - reading);
    }

    // ---- what the profile shows ----

    /// <summary>An IELTS band skill from any number of dated results, in any order.</summary>
    public static SkillDto Band(IEnumerable<ProfileSample> samples)
    {
        var (value, size, trend) = Summarize(samples, BandTrendThreshold);
        return new SkillDto(value, size, trend);
    }

    /// <summary>A percentage skill (Grammar, Vocabulary) from any number of dated results, in any order.</summary>
    public static PercentDto Percent(IEnumerable<ProfileSample> samples)
    {
        var (value, size, trend) = Summarize(samples, PercentTrendThreshold);
        return new PercentDto(value, size, trend);
    }

    /// <summary>Share of counted lessons the student took part in. <see cref="PercentDto.SampleSize"/> is every counted lesson,
    /// not only the newest five: attendance is a running tally, not an average of recent results.</summary>
    public static PercentDto Attendance(IEnumerable<(DateTimeOffset StartsAt, AttendanceStatus Status)> marks)
    {
        var counted = marks.Where(m => CountsForAttendance(m.Status)).OrderByDescending(m => m.StartsAt).ToList();
        var value = Rate(counted.Count(m => Attended(m.Status)), counted.Count);
        var trend = Trend(counted.Select(m => Attended(m.Status) ? 100m : 0m).ToList(), PercentTrendThreshold, CountTrendGroup, CountTrendMinSamples);
        return new PercentDto(value, counted.Count, trend);
    }

    /// <summary>Share of the homework given to the student that they handed in — the whole given set, as the student overview does.
    /// The trend looks only at homework already due, newest deadline first: a task still open is not a failure to trend on.</summary>
    public static PercentDto HomeworkCompletion(IEnumerable<(DateTimeOffset Deadline, bool Submitted)> assigned, DateTimeOffset now)
    {
        var all = assigned.ToList();
        var value = Rate(all.Count(h => h.Submitted), all.Count);
        var due = all.Where(h => h.Deadline <= now).OrderByDescending(h => h.Deadline).Select(h => h.Submitted ? 100m : 0m).ToList();
        return new PercentDto(value, all.Count, Trend(due, PercentTrendThreshold, CountTrendGroup, CountTrendMinSamples));
    }

    private static (decimal? Value, int SampleSize, int? Trend) Summarize(IEnumerable<ProfileSample> samples, decimal trendThreshold)
    {
        // Enough for the average (5) and for the trend (3 + 3); the sort is stable, so same-day results keep the order they came in.
        var newestFirst = samples.OrderByDescending(s => s.At).Take(2 * TrendGroup).Select(s => s.Value).ToList();
        var average = WeightedAverage(newestFirst);
        return (average is null ? null : Math.Round(average.Value, 1), Math.Min(newestFirst.Count, Window), Trend(newestFirst, trendThreshold));
    }
}
