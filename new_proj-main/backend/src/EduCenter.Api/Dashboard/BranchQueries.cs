using EduCenter.Api.Academic;
using EduCenter.Api.Automation;
using EduCenter.Api.Users;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Dashboard;

public record ExamTrendPointDto(string Month, decimal Average, int Results);

/// <summary>The learning results of one branch in a period. Bands (Listening…Overall) are 0–9, Grammar and Vocabulary percentages 0–100;
/// any of them is null when nothing was graded for it, never zero. AttendanceRate is a 0–1 fraction, like the rest of the dashboard.</summary>
public record AcademicBlockDto(
    decimal? Listening, decimal? Reading, decimal? Writing, decimal? Speaking, decimal? Overall,
    decimal? Grammar, decimal? Vocabulary, int Results,
    List<ExamTrendPointDto> Trend,
    int LessonsHeld, int LessonsCancelled, double AttendanceRate);

/// <param name="RetentionRate">Of the students already in the teacher's groups when the period began (leavers by graduation left out),
/// the share whose enrollment is still active; null when there were none to keep.</param>
public record BranchTeacherRowDto(
    Guid TeacherId, string FullName, int Groups, int LessonsHeld, int Attended, int Counted, double AttendanceRate,
    double? AverageReviewHours, int HomeworkPendingReview,
    int RetentionBase, int Retained, double? RetentionRate,
    int OpenCases, int ClosedCases, int? Rank);

public record TeachersBlockDto(List<BranchTeacherRowDto> Teachers);

/// <summary>
/// The heavy reads behind a branch's dashboard. Everything is counted in the database (grouped SQL) or from a few thousand lesson rows —
/// never from one row per attendance mark or per exam result — and the period is cut on the branch's own calendar days as an instant range.
/// </summary>
public static class BranchQueries
{
    private static double Rate(int part, int whole) => whole == 0 ? 0 : Math.Round((double)part / whole, 4);
    private static decimal? Round(decimal? value, int digits = 2) => value is null ? null : Math.Round(value.Value, digits);

    // ── Academic ────────────────────────────────────────────────────────────────────────────────────

    public static async Task<AcademicBlockDto> AcademicAsync(
        AppDbContext db, Guid branchId, string? timezone, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var results = from r in db.ExamResults
                      join e in db.Exams on r.ExamId equals e.Id
                      join g in db.Groups on e.GroupId equals g.Id
                      where g.BranchId == branchId
                      select new { r, e.Date };

        var scores = await results.Where(x => x.Date >= from && x.Date <= to)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Listening = g.Average(x => x.r.Listening), Reading = g.Average(x => x.r.Reading),
                Writing = g.Average(x => x.r.Writing), Speaking = g.Average(x => x.r.Speaking),
                Overall = g.Average(x => x.r.Overall),
                Grammar = g.Average(x => x.r.Grammar), Vocabulary = g.Average(x => x.r.Vocabulary),
                Results = g.Count(),
            })
            .FirstOrDefaultAsync(ct);

        // The last six calendar months up to the end of the period — a trend needs more than the (often one-month) period itself.
        var trendFrom = new DateOnly(to.Year, to.Month, 1).AddMonths(-5);
        var trend = (await results.Where(x => x.Date >= trendFrom && x.Date <= to && x.r.Overall != null)
                .GroupBy(x => new { x.Date.Year, x.Date.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Average = g.Average(x => x.r.Overall), Results = g.Count() })
                .ToListAsync(ct))
            .OrderBy(m => m.Year).ThenBy(m => m.Month)
            .Select(m => new ExamTrendPointDto($"{m.Year:D4}-{m.Month:D2}", Math.Round(m.Average!.Value, 2), m.Results))
            .ToList();

        var start = BranchClock.StartOfLocalDay(timezone, from);
        var end = BranchClock.StartOfLocalDay(timezone, to.AddDays(1));
        var lessons = await db.Lessons.Where(l => l.BranchId == branchId && l.StartsAt >= start && l.StartsAt < end)
            .GroupBy(l => l.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var attendance = await (
                from a in db.Attendance
                join l in db.Lessons.Held() on a.LessonId equals l.Id
                where l.BranchId == branchId && l.StartsAt >= start && l.StartsAt < end
                group a by 1 into g
                select new
                {
                    Attended = g.Count(a => a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late || a.Status == AttendanceStatus.Online),
                    Counted = g.Count(a => a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late
                        || a.Status == AttendanceStatus.Online || a.Status == AttendanceStatus.Absent),
                })
            .FirstOrDefaultAsync(ct);

        return new AcademicBlockDto(
            Round(scores?.Listening), Round(scores?.Reading), Round(scores?.Writing), Round(scores?.Speaking), Round(scores?.Overall),
            Round(scores?.Grammar, 1), Round(scores?.Vocabulary, 1), scores?.Results ?? 0,
            trend,
            lessons.Where(l => l.Status != LessonStatus.Cancelled).Sum(l => l.Count),
            lessons.Where(l => l.Status == LessonStatus.Cancelled).Sum(l => l.Count),
            Rate(attendance?.Attended ?? 0, attendance?.Counted ?? 0));
    }

    // ── Teachers ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One row per teacher (or assistant) of the branch's groups. The per-group figures are the ones the Teacher 360° tabs show
    /// (<see cref="TeacherWorkloadQueries"/>), added up over the groups the person staffs, so the two screens cannot disagree.</summary>
    public static async Task<TeachersBlockDto> TeachersAsync(
        AppDbContext db, Guid branchId, string? timezone, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var groups = await db.Groups.Where(g => g.BranchId == branchId).Select(g => new StaffedGroup(g.Id, g.Name, g.BranchId, default)).ToListAsync(ct);
        var groupIds = groups.Select(g => g.GroupId).ToList();
        var staff = await (
            from gs in db.GroupStaff
            join u in db.Users on gs.UserId equals u.Id
            where groupIds.Contains(gs.GroupId)
            select new { gs.GroupId, gs.UserId, u.FullName }).ToListAsync(ct);
        if (staff.Count == 0) return new TeachersBlockDto([]);

        var period = new TeacherPeriod(from, to);
        var attendance = (await TeacherWorkloadQueries.AttendanceAsync(db, groups, period, ct)).ToDictionary(a => a.GroupId);
        var homework = (await TeacherWorkloadQueries.HomeworkRawAsync(db, groupIds, period, ct)).ToDictionary(h => h.GroupId);

        var start = BranchClock.StartOfLocalDay(timezone, from);
        var end = BranchClock.StartOfLocalDay(timezone, to.AddDays(1));

        // Who was already in each group when the period began, and where they are now. A student who finished the course did not leave.
        var enrolments = await db.Enrollments
            .Where(e => groupIds.Contains(e.GroupId) && e.EnrolledAt < start && (e.EndedAt == null || e.EndedAt >= start)
                && e.Status != EnrollmentStatus.Completed)
            .Select(e => new { e.GroupId, e.StudentId, e.Status }).ToListAsync(ct);

        var cases = await db.SupportCases.Where(c => groupIds.Contains(c.GroupId))
            .Select(c => new { c.GroupId, c.Status, c.ClosedAt }).ToListAsync(ct);

        var rows = staff.GroupBy(s => s.UserId).Select(person =>
        {
            var mine = person.Select(s => s.GroupId).ToHashSet();
            var att = mine.Select(id => attendance[id]).ToList();
            var hw = mine.Select(id => homework[id]).ToList();
            var attended = att.Sum(a => a.Attended);
            var counted = att.Sum(a => a.Counted);

            var starters = enrolments.Where(e => mine.Contains(e.GroupId)).GroupBy(e => e.StudentId).ToList();
            var retained = starters.Count(s => s.Any(e => e.Status == EnrollmentStatus.Active));

            var myCases = cases.Where(c => mine.Contains(c.GroupId)).ToList();
            return new BranchTeacherRowDto(
                person.Key, person.First().FullName, mine.Count,
                att.Sum(a => a.LessonsHeld), attended, counted, Rate(attended, counted),
                TeacherWorkloadQueries.MeanHours(hw.Sum(h => h.Reviewed), hw.Sum(h => h.ReviewHours)), hw.Sum(h => h.Pending),
                starters.Count, retained, starters.Count == 0 ? null : Rate(retained, starters.Count),
                myCases.Count(c => c.Status is SupportCaseStatus.Recommended or SupportCaseStatus.Assigned),
                myCases.Count(c => c.Status is SupportCaseStatus.Dismissed or SupportCaseStatus.Resolved && c.ClosedAt >= start && c.ClosedAt < end),
                null);
        }).ToList();

        // The league table: by attendance, the teachers whose groups have any marks at all first.
        var ranked = rows.OrderByDescending(r => r.Counted > 0).ThenByDescending(r => r.AttendanceRate).ThenBy(r => r.FullName).ToList();
        return new TeachersBlockDto(ranked.Select((r, i) => r with { Rank = r.Counted > 0 ? i + 1 : null }).ToList());
    }
}
