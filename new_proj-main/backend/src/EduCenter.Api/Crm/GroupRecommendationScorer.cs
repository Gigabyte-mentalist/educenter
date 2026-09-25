using System.Text.RegularExpressions;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Crm.Domain;

namespace EduCenter.Api.Crm;

/// <summary>The course levels in the order a person moves through them, and how to spot one in free text — a lead's
/// "B1" or a group named "General English A1". There is no level column on a course, so the name is the source.</summary>
public static class LevelScale
{
    public static readonly IReadOnlyList<string> Order =
        ["A1", "A2", "B1", "B2", "C1", "IELTS Foundation", "IELTS Intermediate", "IELTS Advanced"];

    // Whole-token match: "A1" must not be found inside "B1A1x". Spaces inside a level ("IELTS Foundation") match any run of whitespace.
    private static readonly Regex[] Patterns = Order
        .Select(level => new Regex($@"(?<![A-Za-z0-9]){level.Replace(" ", @"\s+")}(?![A-Za-z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled))
        .ToArray();

    /// <summary>The level named earliest in <paramref name="text"/>, or null when it names none.</summary>
    public static int? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        int? best = null;
        var bestAt = int.MaxValue;
        for (var i = 0; i < Patterns.Length; i++)
        {
            var match = Patterns[i].Match(text);
            if (match.Success && match.Index < bestAt)
            {
                best = i;
                bestAt = match.Index;
            }
        }
        return best;
    }

    /// <summary>Same level 100%, one step away 60%, further 0% (docs/21.09-audit-jurnali/S06.01).</summary>
    public static double Closeness(int leadLevel, int groupLevel) => Math.Abs(leadLevel - groupLevel) switch
    {
        0 => 1.0,
        1 => 0.6,
        _ => 0.0,
    };
}

/// <summary>Which of the <see cref="PreferredTimeCodes"/> a lesson falls in, judged on the branch's local clock.</summary>
public static class LessonTimeBuckets
{
    public static IEnumerable<string> Of(DateTime localStart)
    {
        var hour = localStart.Hour;
        if (hour is >= 8 and < 12) yield return PreferredTimeCodes.Morning;
        else if (hour is >= 12 and < 17) yield return PreferredTimeCodes.Afternoon;
        else if (hour is >= 17 and < 21) yield return PreferredTimeCodes.Evening;

        if (localStart.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) yield return PreferredTimeCodes.Weekend;
    }

    public static string Label(string code) => code switch
    {
        PreferredTimeCodes.Morning => "ertalab",
        PreferredTimeCodes.Afternoon => "tushdan keyin",
        PreferredTimeCodes.Evening => "kechqurun",
        PreferredTimeCodes.Weekend => "dam olish kunlari",
        _ => code,
    };
}

public record GroupCandidate(
    Guid GroupId, string GroupName, Guid BranchId, int Capacity, int FreeSeats, GroupStatus Status,
    string CourseName, string CourseLevel);

/// <summary>What the operator knows about the person; blank fields are simply not used.</summary>
public record LeadProfile(Guid BranchId, string InterestedCourse, string? CurrentLevel, IReadOnlyCollection<string> PreferredTimes);

/// <summary>
/// docs/00_TZ.md §12 Group Recommendation Engine, six factors. A factor with nothing to compare — the lead gave no level, the
/// group's name names none, the lead has no preferred time or the group has no lessons coming — is <b>left out</b> and the others
/// are rescaled to 100%, so a missing fact never turns into a fake score.
/// </summary>
public static class GroupRecommendationScorer
{
    private const double CourseWeight = 35, LevelWeight = 20, BranchWeight = 15, TimeWeight = 15, SeatsWeight = 10, ActiveWeight = 5;

    /// <param name="upcomingLocalStarts">Start of each of the group's next lessons on the branch clock.</param>
    /// <returns>The score as 0–100, <b>not rounded</b>: the list is ranked on it, and two groups that both round to 92 are not tied.</returns>
    public static (double Percent, List<string> Reasons) Score(GroupCandidate group, LeadProfile lead, IReadOnlyCollection<DateTime> upcomingLocalStarts)
    {
        var reasons = new List<string>();
        double earned = 0, possible = 0;

        void Factor(double weight, double fraction)
        {
            possible += weight;
            earned += weight * fraction;
        }

        var interest = lead.InterestedCourse.Trim();
        var courseMatch = interest.Length > 0 &&
            (group.CourseName.Contains(interest, StringComparison.OrdinalIgnoreCase)
                || group.CourseLevel.Contains(interest, StringComparison.OrdinalIgnoreCase)
                || interest.Contains(group.CourseName, StringComparison.OrdinalIgnoreCase));
        Factor(CourseWeight, courseMatch ? 1 : 0);
        if (courseMatch) reasons.Add("Qiziqqan kursiga mos");

        var leadLevel = LevelScale.Find(lead.CurrentLevel);
        var groupLevel = LevelScale.Find(group.GroupName) ?? LevelScale.Find(group.CourseName);
        if (leadLevel is { } wanted && groupLevel is { } offered)
        {
            var closeness = LevelScale.Closeness(wanted, offered);
            Factor(LevelWeight, closeness);
            if (closeness >= 1) reasons.Add($"Daraja mos ({LevelScale.Order[offered]})");
            else if (closeness > 0) reasons.Add($"Daraja yaqin ({LevelScale.Order[offered]})");
        }

        var sameBranch = group.BranchId == lead.BranchId;
        Factor(BranchWeight, sameBranch ? 1 : 0);
        if (sameBranch) reasons.Add("O'zi murojaat qilgan filialda");

        if (lead.PreferredTimes.Count > 0 && upcomingLocalStarts.Count > 0)
        {
            var matchedBuckets = new HashSet<string>();
            var matchingLessons = 0;
            foreach (var start in upcomingLocalStarts)
            {
                var fit = LessonTimeBuckets.Of(start).Where(lead.PreferredTimes.Contains).ToList();
                if (fit.Count == 0) continue;
                matchingLessons++;
                matchedBuckets.UnionWith(fit);
            }

            // Every upcoming lesson in the person's window is a full fit; a group that meets at mixed times fits in proportion.
            Factor(TimeWeight, (double)matchingLessons / upcomingLocalStarts.Count);
            if (matchingLessons == upcomingLocalStarts.Count)
                reasons.Add($"Qulay vaqtda ({string.Join(", ", PreferredTimeCodes.All.Where(matchedBuckets.Contains).Select(LessonTimeBuckets.Label))})");
            else if (matchingLessons > 0)
                reasons.Add("Ba'zi darslari qulay vaqtda");
        }

        if (group.FreeSeats > 0)
        {
            Factor(SeatsWeight, (double)Math.Min(group.FreeSeats, group.Capacity) / group.Capacity);
            reasons.Add(group.FreeSeats == 1 ? "1 ta o'rin bo'sh" : $"{group.FreeSeats} ta o'rin bo'sh");
        }

        var active = group.Status == GroupStatus.Active;
        Factor(ActiveWeight, active ? 1 : 0);
        if (active) reasons.Add("Guruh allaqachon faol");

        var percent = possible == 0 ? 0 : 100 * earned / possible;
        return (Math.Clamp(percent, 0, 100), reasons);
    }
}
