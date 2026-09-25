using EduCenter.Api.Academic;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Api.Lms;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Users;

/// <summary>A group the teacher staffs, as the caller may see it.</summary>
public sealed record StaffedGroup(Guid GroupId, string GroupName, Guid BranchId, GroupStaffRole Role);

/// <summary>The report window: whole calendar days, read in each branch's own timezone (a lesson at 01:00 Tashkent time
/// on the 1st is not last month's lesson). Rows are fetched a day wide on each side and then cut by the branch's calendar,
/// the way the dashboard does it.</summary>
public sealed record TeacherPeriod(DateOnly From, DateOnly To)
{
    public const int MaxDays = 366;

    public DateTimeOffset FetchFrom => new(From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    public DateTimeOffset FetchTo => new(To.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    public bool Contains(string? timezone, DateTimeOffset instant)
    {
        var local = BranchClock.LocalDate(timezone, instant);
        return local >= From && local <= To;
    }

    /// <summary>The last 30 days when nothing was asked for; null (with the reason) when the range itself is not acceptable.</summary>
    public static TeacherPeriod? Resolve(DateOnly? from, DateOnly? to, DateTimeOffset now, out string? error)
    {
        var end = to ?? DateOnly.FromDateTime(now.UtcDateTime);
        var start = from ?? end.AddDays(-30);
        error = null;
        if (end < start) { error = "Tugash sanasi boshlanish sanasidan oldin bo'lmasin."; return null; }
        if (end.DayNumber - start.DayNumber > MaxDays) { error = "Hisobot davri bir yildan oshmasligi kerak."; return null; }
        return new TeacherPeriod(start, end);
    }
}

/// <param name="Attended">Marks of present, late and online; <paramref name="Counted"/> adds the absent ones (excused is neither).</param>
public record WorkloadGroupAttendanceDto(
    Guid GroupId, string GroupName, int LessonsHeld, int LessonsCancelled, int Attended, int Counted, double AttendanceRate);

public record WorkloadGroupHomeworkDto(Guid GroupId, string GroupName, int Created, int PendingReview, double? AverageReviewHours);

public record WorkloadMonthDto(string Month, decimal Average, int Results);

public record WorkloadGroupExamDto(Guid GroupId, string GroupName, int Exams, int Results, decimal? AverageOverall, List<WorkloadMonthDto> Trend);

/// <param name="AttendanceRate">Percent 0–100, the same convention as <see cref="TeacherPerformanceDto"/>.</param>
/// <param name="AverageReviewHours">Mean of ReviewedAt − SubmittedAt over the reviewed submissions; null when nothing was reviewed.</param>
public record TeacherWorkloadDto(
    DateOnly From, DateOnly To,
    int LessonsHeld, int LessonsCancelled, double AttendanceRate,
    int HomeworkCreated, int HomeworkPendingReview, double? AverageReviewHours,
    List<WorkloadGroupAttendanceDto> Attendance,
    List<WorkloadGroupHomeworkDto> Homework,
    List<WorkloadGroupExamDto> Exams);

/// <summary>
/// The numbers behind Teacher 360°: how a teacher's groups attend, how fast their homework gets marked, how their groups do in
/// exams. Every screen that shows one of these figures — the workload tabs and the performance snapshot — asks here, so the
/// same teacher never gets two different attendance percentages.
/// A lesson counts only if it was not cancelled (<see cref="LessonQueries.Held"/>); excused absences leave the denominator;
/// homework and lessons are placed in the period by the date of the lesson, exams by the exam's own date.
/// </summary>
public static class TeacherWorkloadQueries
{
    private static readonly AttendanceStatus[] AttendedStatuses =
        [AttendanceStatus.Present, AttendanceStatus.Late, AttendanceStatus.Online];
    private static readonly AttendanceStatus[] CountedStatuses =
        [AttendanceStatus.Present, AttendanceStatus.Late, AttendanceStatus.Online, AttendanceStatus.Absent];

    public static double Percent(int part, int whole) => whole == 0 ? 0 : Math.Round(part * 100.0 / whole, 1);

    /// <summary>The groups this teacher staffs that the caller may see: all of them for the organization and for the teacher
    /// themselves, only the ones in the caller's branches for a branch administrator.</summary>
    public static async Task<List<StaffedGroup>> VisibleGroupsAsync(
        this AppDbContext db, ResolvedScope scope, Guid teacherId, CancellationToken ct = default)
    {
        var rows = await (
            from gs in db.GroupStaff
            join g in db.Groups on gs.GroupId equals g.Id
            where gs.UserId == teacherId && g.OrganizationId == scope.OrganizationId
            orderby g.Name
            select new StaffedGroup(g.Id, g.Name, g.BranchId, gs.Role)
        ).ToListAsync(ct);

        return scope.Scope == ScopeType.MyBranches ? rows.Where(r => scope.BranchIds.Contains(r.BranchId)).ToList() : rows;
    }

    /// <summary>A lesson of one of the groups, with the timezone of the branch that holds it.</summary>
    private sealed record LessonRow(Guid Id, Guid GroupId, string? Timezone, DateTimeOffset StartsAt, bool Cancelled);

    /// <summary>Held and cancelled lessons of these groups in the period (all of them when there is no period).</summary>
    private static async Task<List<LessonRow>> LessonsAsync(AppDbContext db, List<Guid> groupIds, TeacherPeriod? period, CancellationToken ct)
    {
        var query = from l in db.Lessons
                    join b in db.Branches on l.BranchId equals b.Id
                    where groupIds.Contains(l.GroupId)
                    select new { l.Id, l.GroupId, b.Timezone, l.StartsAt, l.Status };
        if (period is not null) query = query.Where(l => l.StartsAt >= period.FetchFrom && l.StartsAt < period.FetchTo);

        var rows = (await query.ToListAsync(ct))
            .Select(l => new LessonRow(l.Id, l.GroupId, l.Timezone, l.StartsAt, l.Status == LessonStatus.Cancelled));
        return (period is null ? rows : rows.Where(r => period.Contains(r.Timezone, r.StartsAt))).ToList();
    }

    /// <summary>Attendance the way a centre counts it: present, late and online attended; absent missed; excused is neither. The marks are
    /// counted in the database, one row per lesson — a branch with thousands of students has hundreds of thousands of marks and only a few
    /// thousand lessons.</summary>
    public static async Task<List<WorkloadGroupAttendanceDto>> AttendanceAsync(
        AppDbContext db, List<StaffedGroup> groups, TeacherPeriod? period, CancellationToken ct = default)
    {
        var lessons = await LessonsAsync(db, groups.Select(g => g.GroupId).ToList(), period, ct);
        var heldIds = lessons.Where(l => !l.Cancelled).Select(l => l.Id).ToList();
        var groupOfLesson = lessons.ToDictionary(l => l.Id, l => l.GroupId);

        var perLesson = await db.Attendance.Where(a => heldIds.Contains(a.LessonId))
            .GroupBy(a => a.LessonId)
            .Select(g => new
            {
                LessonId = g.Key,
                Attended = g.Count(a => a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late || a.Status == AttendanceStatus.Online),
                Counted = g.Count(a => a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late
                    || a.Status == AttendanceStatus.Online || a.Status == AttendanceStatus.Absent),
            })
            .ToListAsync(ct);
        var marksByGroup = perLesson.ToLookup(m => groupOfLesson[m.LessonId]);

        return groups.Select(g =>
        {
            var attended = marksByGroup[g.GroupId].Sum(m => m.Attended);
            var counted = marksByGroup[g.GroupId].Sum(m => m.Counted);
            return new WorkloadGroupAttendanceDto(
                g.GroupId, g.GroupName,
                lessons.Count(l => l.GroupId == g.GroupId && !l.Cancelled),
                lessons.Count(l => l.GroupId == g.GroupId && l.Cancelled),
                attended, counted, Percent(attended, counted));
        }).ToList();
    }

    /// <summary>What was given at the lessons of one group in the period and how it was marked — sums, so groups can be added up
    /// (into a teacher, into a branch) without averaging averages.</summary>
    public sealed record GroupHomeworkRaw(Guid GroupId, int Created, int Pending, int Reviewed, double ReviewHours);

    /// <summary>The homework given at lessons of these groups in the period, how many submissions still wait for the teacher's mark, and how
    /// long the marked ones waited.</summary>
    public static async Task<List<GroupHomeworkRaw>> HomeworkRawAsync(
        AppDbContext db, List<Guid> groupIds, TeacherPeriod period, CancellationToken ct = default)
    {
        var homework = (await (
                from h in db.Homework
                join l in db.Lessons on h.LessonId equals l.Id
                join b in db.Branches on l.BranchId equals b.Id
                where groupIds.Contains(l.GroupId) && l.StartsAt >= period.FetchFrom && l.StartsAt < period.FetchTo
                select new { h.Id, l.GroupId, l.StartsAt, b.Timezone }).ToListAsync(ct))
            .Where(h => period.Contains(h.Timezone, h.StartsAt))
            .ToList();

        var homeworkIds = homework.Select(h => h.Id).ToList();
        var groupOfHomework = homework.ToDictionary(h => h.Id, h => h.GroupId);
        var submissions = await db.HomeworkSubmissions.Where(s => homeworkIds.Contains(s.HomeworkId))
            .Select(s => new { s.HomeworkId, s.SubmittedAt, s.ReviewedAt }).ToListAsync(ct);
        var submissionsByGroup = submissions.ToLookup(s => groupOfHomework[s.HomeworkId]);

        return groupIds.Select(groupId =>
        {
            var mine = submissionsByGroup[groupId].ToList();
            var reviewed = mine.Where(s => s.ReviewedAt != null).ToList();
            return new GroupHomeworkRaw(
                groupId, homework.Count(h => h.GroupId == groupId), mine.Count - reviewed.Count, reviewed.Count,
                reviewed.Sum(s => (s.ReviewedAt!.Value - s.SubmittedAt).TotalHours));
        }).ToList();
    }

    public static double? MeanHours(int reviewed, double hours) => reviewed == 0 ? null : Math.Round(hours / reviewed, 1);

    /// <summary>The homework of the period per group, and over all of them together.</summary>
    public static async Task<(List<WorkloadGroupHomeworkDto> Groups, double? AverageReviewHours)> HomeworkAsync(
        AppDbContext db, List<StaffedGroup> groups, TeacherPeriod period, CancellationToken ct = default)
    {
        var raw = await HomeworkRawAsync(db, groups.Select(g => g.GroupId).ToList(), period, ct);
        var byGroup = raw.ToDictionary(r => r.GroupId);
        var perGroup = groups.Select(g =>
        {
            var r = byGroup[g.GroupId];
            return new WorkloadGroupHomeworkDto(g.GroupId, g.GroupName, r.Created, r.Pending, MeanHours(r.Reviewed, r.ReviewHours));
        }).ToList();
        return (perGroup, MeanHours(raw.Sum(r => r.Reviewed), raw.Sum(r => r.ReviewHours)));
    }

    /// <summary>The overall band of one exam result, with the group and the date of the exam it belongs to.</summary>
    private sealed record ExamScoreRow(Guid GroupId, Guid ExamId, DateOnly Date, decimal Overall);

    private static async Task<List<ExamScoreRow>> ExamScoresAsync(AppDbContext db, List<Guid> groupIds, TeacherPeriod? period, CancellationToken ct)
    {
        var query = from r in db.ExamResults
                    join e in db.Exams on r.ExamId equals e.Id
                    where groupIds.Contains(e.GroupId) && r.Overall != null
                    select new { e.GroupId, ExamId = e.Id, e.Date, Overall = r.Overall!.Value };
        if (period is not null) query = query.Where(x => x.Date >= period.From && x.Date <= period.To);
        return (await query.ToListAsync(ct)).Select(x => new ExamScoreRow(x.GroupId, x.ExamId, x.Date, x.Overall)).ToList();
    }

    public static async Task<List<WorkloadGroupExamDto>> ExamsAsync(
        AppDbContext db, List<StaffedGroup> groups, TeacherPeriod period, CancellationToken ct = default)
    {
        var scores = await ExamScoresAsync(db, groups.Select(g => g.GroupId).ToList(), period, ct);
        return groups.Select(g =>
        {
            var mine = scores.Where(s => s.GroupId == g.GroupId).ToList();
            var trend = mine.GroupBy(s => $"{s.Date.Year:D4}-{s.Date.Month:D2}").OrderBy(m => m.Key)
                .Select(m => new WorkloadMonthDto(m.Key, Math.Round(m.Average(s => s.Overall), 2), m.Count())).ToList();
            return new WorkloadGroupExamDto(
                g.GroupId, g.GroupName,
                mine.Select(s => s.ExamId).Distinct().Count(), mine.Count,
                mine.Count == 0 ? null : Math.Round(mine.Average(s => s.Overall), 2),
                trend);
        }).ToList();
    }

    /// <summary>The total is the rate over all the groups' marks together — not an average of the per-group percentages,
    /// which would let a two-student group weigh as much as a fifteen-student one.</summary>
    private static double TotalRate(List<WorkloadGroupAttendanceDto> attendance) =>
        Percent(attendance.Sum(a => a.Attended), attendance.Sum(a => a.Counted));

    public static async Task<TeacherWorkloadDto> WorkloadAsync(
        AppDbContext db, List<StaffedGroup> groups, TeacherPeriod period, CancellationToken ct = default)
    {
        var attendance = await AttendanceAsync(db, groups, period, ct);
        var (homework, reviewHours) = await HomeworkAsync(db, groups, period, ct);
        var exams = await ExamsAsync(db, groups, period, ct);

        return new TeacherWorkloadDto(
            period.From, period.To,
            attendance.Sum(a => a.LessonsHeld), attendance.Sum(a => a.LessonsCancelled), TotalRate(attendance),
            homework.Sum(h => h.Created), homework.Sum(h => h.PendingReview), reviewHours,
            attendance, homework, exams);
    }

    /// <summary>The all-time snapshot on the Performance tab, over exactly the groups the caller may see this teacher through.</summary>
    public static async Task<TeacherPerformanceDto> PerformanceAsync(
        AppDbContext db, List<StaffedGroup> groups, CancellationToken ct = default)
    {
        var groupIds = groups.Select(g => g.GroupId).ToList();

        var studentIds = await db.Enrollments
            .Where(e => groupIds.Contains(e.GroupId) && e.Status == EnrollmentStatus.Active)
            .Select(e => e.StudentId).Distinct().ToListAsync(ct);

        var attendanceRate = TotalRate(await AttendanceAsync(db, groups, null, ct));

        // How many of the (homework, student) pairs the teacher's homework put on their students were actually handed in.
        // A pair exists only where the student was given the homework: everyone in its group, unless it is an individual task.
        var assignedHomework = (await (
            from h in db.Homework
            join l in db.Lessons on h.LessonId equals l.Id
            where groupIds.Contains(l.GroupId)
            select new { h.Id, l.GroupId }
        ).ToListAsync(ct)).Select(h => new HomeworkRef(h.Id, h.GroupId)).ToList();
        var assignedHomeworkIds = assignedHomework.Select(h => h.Id).ToList();
        var possibleSubmissions = await db.CountAssignmentsAsync(assignedHomework, ct);
        var actualSubmissions = possibleSubmissions == 0 ? 0 : await db.HomeworkSubmissions
            .CountAsync(s => assignedHomeworkIds.Contains(s.HomeworkId) && studentIds.Contains(s.StudentId), ct);
        var homeworkRate = possibleSubmissions == 0 ? 0 : Math.Round(actualSubmissions * 100.0 / possibleSubmissions, 1);

        var examScores = (await ExamScoresAsync(db, groupIds, null, ct)).Select(s => s.Overall).ToList();
        decimal? averageExam = examScores.Count == 0 ? null : Math.Round(examScores.Average(), 2);

        return new TeacherPerformanceDto(studentIds.Count, groupIds.Count, attendanceRate, homeworkRate, averageExam);
    }
}
