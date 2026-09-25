using EduCenter.Api.Academic;
using EduCenter.Api.Authorization;
using EduCenter.Api.Lms;
using EduCenter.Api.Ai;
using EduCenter.Api.Automation;
using EduCenter.Api.Crm;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Ai.Domain;
using EduCenter.Modules.Crm.Domain;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Lms.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Dashboard;

/// <summary>
/// The screen every role lands on (docs/00_TZ.md §5, §3.4, §3.6, §24). Each endpoint answers one question —
/// "what does this person need to see first" — and the caller's <see cref="ResolvedScope"/> decides how much of
/// the organization that covers, so a branch administrator and the super admin call the same URL and get their
/// own slice. The frontend picks the endpoint from the role it already knows; the server never reads a role name.
/// </summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController(AppDbContext db, PermissionResolver permissions, BranchDashboardCache branchCache) : ControllerBase
{
    /// <summary>Attendance counted the way a centre counts it: present, late and online are attendance; excused
    /// is neither a win nor a miss, so it leaves the denominator entirely.</summary>
    private static readonly AttendanceStatus[] Attended =
        [AttendanceStatus.Present, AttendanceStatus.Late, AttendanceStatus.Online];

    private static readonly AttendanceStatus[] Counted =
        [AttendanceStatus.Present, AttendanceStatus.Late, AttendanceStatus.Online, AttendanceStatus.Absent];

    private static double Rate(int part, int whole) => whole == 0 ? 0 : Math.Round((double)part / whole, 4);

    /// <summary>A lesson row plus the timezone of the branch that holds it — "today" is a branch-local calendar
    /// day, so the window can only be applied once the timezone is known.</summary>
    private sealed record LessonRow(DashboardLessonDto Dto, string Timezone);

    private async Task<List<LessonRow>> LessonRowsAsync(IQueryable<Lesson> lessons, CancellationToken ct) =>
        await (from l in lessons
               join g in db.Groups on l.GroupId equals g.Id
               join b in db.Branches on l.BranchId equals b.Id
               join u in db.Users on l.TeacherId equals u.Id into staff
               from u in staff.DefaultIfEmpty()
               orderby l.StartsAt
               select new LessonRow(
                   new DashboardLessonDto(l.Id, l.GroupId, g.Name, b.Name, l.RoomName, l.StartsAt, l.EndsAt, l.Topic,
                       l.TeacherId, u != null ? u.FullName : "Tayinlanmagan"),
                   b.Timezone))
            .ToListAsync(ct);

    private static List<DashboardLessonDto> OnLocalDay(IEnumerable<LessonRow> rows, DateTimeOffset now, int dayOffset = 0) =>
        rows.Where(r => BranchClock.LocalDate(r.Timezone, r.Dto.StartsAt)
                        == BranchClock.Today(r.Timezone, now).AddDays(dayOffset))
            .Select(r => r.Dto)
            .OrderBy(d => d.StartsAt)
            .ToList();

    // Organization ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Super Admin's global view and an Administrator's branch view — the same numbers, cut to scope.</summary>
    [HttpGet("organization")]
    [Authorize(Policy = "dashboard.view")]
    public async Task<ActionResult<OrganizationDashboardDto>> Organization(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? branchId, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope is not (ScopeType.AllOrganization or ScopeType.MyBranches)) return Forbid();

        var period = ResolvePeriod(from, to, out var problem);
        if (period is null) return BadRequest(new { error = problem });

        var (dto, failure) = await BuildOrganizationAsync(scope, period.Value.Start, period.Value.End, branchId, ct);
        if (dto is null) return failure!;

        // The AI card is for those who may read the AI usage report; nobody else is told what it costs.
        var canSeeAi = await permissions.ResolveAsync(scope.UserId, "ai.usage.view", ct) is not null;
        return Ok(canSeeAi ? dto : WithoutAi(dto));
    }

    private static OrganizationDashboardDto WithoutAi(OrganizationDashboardDto dto) =>
        dto with { AiRequests = null, AiCostUsd = null, AiActiveStudents = null };

    /// <summary>The report period of a dashboard: the caller's dates, or the current month; at most a year long.</summary>
    private static (DateOnly Start, DateOnly End)? ResolvePeriod(DateOnly? from, DateOnly? to, out string? problem)
    {
        var utcToday = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var start = from ?? new DateOnly(utcToday.Year, utcToday.Month, 1);
        var end = to ?? start.AddMonths(1).AddDays(-1);
        problem = null;
        if (end < start) { problem = "Tugash sanasi boshlanish sanasidan oldin bo'lmasin."; return null; }
        if (end.DayNumber - start.DayNumber > 366) { problem = "Hisobot davri bir yildan oshmasligi kerak."; return null; }
        return (start, end);
    }

    /// <summary>The organization dashboard of the caller's scope, optionally cut to one branch — the body of the organization endpoint,
    /// and the "Umumiy" and money half of a single branch's dashboard, so the two never count the same thing differently.</summary>
    private async Task<(OrganizationDashboardDto? Dto, ActionResult? Failure)> BuildOrganizationAsync(
        ResolvedScope scope, DateOnly start, DateOnly end, Guid? branchId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var utcToday = DateOnly.FromDateTime(now.UtcDateTime);

        var branchQuery = db.Branches.Where(b => b.OrganizationId == scope.OrganizationId);
        if (scope.Scope == ScopeType.MyBranches) branchQuery = branchQuery.Where(b => scope.BranchIds.Contains(b.Id));
        if (branchId is not null)
        {
            if (!await branchQuery.AnyAsync(b => b.Id == branchId, ct)) return (null, Forbid());
            branchQuery = branchQuery.Where(b => b.Id == branchId);
        }

        var branches = await branchQuery.OrderBy(b => b.Name)
            .Select(b => new { b.Id, b.Name, b.Timezone }).ToListAsync(ct);
        var branchIds = branches.Select(b => b.Id).ToList();
        var timezone = branches.ToDictionary(b => b.Id, b => b.Timezone);

        bool InPeriod(Guid branch, DateTimeOffset instant)
        {
            var local = BranchClock.LocalDate(timezone[branch], instant);
            return local >= start && local <= end;
        }

        // Cash is fetched a day wide on each side and then filtered by each branch's own calendar, the same way
        // the finance report does it — a payment at 01:00 Tashkent on the 1st is not last month's money.
        var fetchFrom = new DateTimeOffset(start.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var fetchTo = new DateTimeOffset(end.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Which student sits in which branch, via the group they are enrolled in. A student enrolled in two
        // branches counts once for the organization and once per branch — that is what a per-branch figure means.
        var enrolments = await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            where branchIds.Contains(g.BranchId)
            select new { e.StudentId, g.BranchId }).ToListAsync(ct);

        var studentIds = enrolments.Select(e => e.StudentId).Distinct().ToList();
        var branchOfStudent = enrolments
            .GroupBy(e => e.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.BranchId).Distinct().ToList());

        var students = await db.Students
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Status, s.JoinedDate })
            .ToListAsync(ct);

        var groups = await db.Groups
            .Where(g => branchIds.Contains(g.BranchId))
            .Select(g => new { g.Id, g.BranchId, g.Status })
            .ToListAsync(ct);

        var window = db.Lessons.Held().Where(l => branchIds.Contains(l.BranchId)
            && l.StartsAt >= now.AddDays(-2) && l.StartsAt <= now.AddDays(2));
        var lessonRows = await LessonRowsAsync(window, ct);
        var lessonsToday = OnLocalDay(lessonRows, now);
        var todayIds = lessonsToday.Select(l => l.Id).ToHashSet();
        var lessonBranch = await window.Select(l => new { l.Id, l.BranchId }).ToListAsync(ct);

        var payments = (await db.Payments
                .Where(p => p.OrganizationId == scope.OrganizationId && branchIds.Contains(p.BranchId)
                    && !p.IsVoided && p.PaidAt >= fetchFrom && p.PaidAt < fetchTo)
                .Select(p => new { p.BranchId, p.Amount, p.RefundedAmount, p.PaidAt })
                .ToListAsync(ct))
            .Where(p => InPeriod(p.BranchId, p.PaidAt))
            .ToList();

        var invoices = await (
            from i in db.Invoices
            join g in db.Groups on i.GroupId equals g.Id
            // A split invoice is counted through its parts (S07.05); with it, the same money would be counted twice.
            where branchIds.Contains(g.BranchId) && i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Split
            select new
            {
                g.BranchId,
                i.StudentId,
                i.PeriodStart,
                i.DueDate,
                Balance = i.Amount - i.DiscountAmount - i.CreditedAmount - i.PaidAmount,
                Net = i.Amount - i.DiscountAmount - i.CreditedAmount,
                Discount = i.DiscountAmount,
            }).ToListAsync(ct);

        var leads = await db.Leads
            .Where(l => branchIds.Contains(l.BranchId))
            .Select(l => new { l.Id, l.BranchId, l.Status, l.CreatedAt })
            .ToListAsync(ct);
        var periodLeads = leads.Where(l => InPeriod(l.BranchId, l.CreatedAt)).ToList();
        var leadBranch = leads.ToDictionary(l => l.Id, l => l.BranchId);
        var convertedIds = leads.Where(l => l.Status == LeadStatus.Converted).Select(l => l.Id).ToHashSet();

        var trials = await db.TrialLessons
            .Select(t => new { t.LeadId, t.AttendanceStatus, t.ScheduledAt })
            .ToListAsync(ct);
        var periodTrials = trials
            .Where(t => leadBranch.ContainsKey(t.LeadId) && InPeriod(leadBranch[t.LeadId], t.ScheduledAt))
            .ToList();

        var attendance = await (
            from a in db.Attendance
            join l in db.Lessons.Held() on a.LessonId equals l.Id
            where branchIds.Contains(l.BranchId) && l.StartsAt >= fetchFrom && l.StartsAt < fetchTo
            select new { l.BranchId, a.Status, l.StartsAt }).ToListAsync(ct);
        var periodAttendance = attendance.Where(a => InPeriod(a.BranchId, a.StartsAt)).ToList();

        var openCases = await (
            from c in db.SupportCases
            join g in db.Groups on c.GroupId equals g.Id
            where branchIds.Contains(g.BranchId)
                && (c.Status == SupportCaseStatus.Recommended || c.Status == SupportCaseStatus.Assigned)
            select c.Id).CountAsync(ct);

        var pendingReviews = await (
            from s in db.HomeworkSubmissions
            join h in db.Homework on s.HomeworkId equals h.Id
            join l in db.Lessons on h.LessonId equals l.Id
            where branchIds.Contains(l.BranchId) && s.Score == null
            select s.Id).CountAsync(ct);

        var teacherCount = await (
            from gs in db.GroupStaff
            join g in db.Groups on gs.GroupId equals g.Id
            where branchIds.Contains(g.BranchId) && gs.Role == GroupStaffRole.MainTeacher
            select gs.UserId).Distinct().CountAsync(ct);

        // The same count as the AI usage report's, over the same requests (AiUsageQueries) — the card links to it.
        var ai = await db.Requests(scope, branchIds, branchId, start, end).BriefAsync(ct);

        // Retention: of the students who have been with the centre a month or more, how many are still active.
        // Anyone who joined in the last 30 days has not had the chance to leave yet, so counting them flatters it.
        var settled = students.Where(s => s.JoinedDate <= utcToday.AddDays(-30)).ToList();
        var retention = Rate(settled.Count(s => s.Status == StudentStatus.Active), settled.Count);

        var branchKpis = branches.Select(b =>
        {
            var ids = branchOfStudent.Where(kv => kv.Value.Contains(b.Id)).Select(kv => kv.Key).ToHashSet();
            var mine = students.Where(s => ids.Contains(s.Id)).ToList();
            var branchToday = BranchClock.Today(b.Timezone, now);
            var bPay = payments.Where(p => p.BranchId == b.Id).ToList();
            var bInv = invoices.Where(i => i.BranchId == b.Id).ToList();
            var bAtt = periodAttendance.Where(a => a.BranchId == b.Id).ToList();
            var bLeads = periodLeads.Where(l => l.BranchId == b.Id).ToList();
            return new BranchKpiDto(
                b.Id, b.Name,
                mine.Count,
                groups.Count(g => g.BranchId == b.Id && g.Status == GroupStatus.Active),
                lessonBranch.Count(l => l.BranchId == b.Id && todayIds.Contains(l.Id)),
                bPay.Sum(p => p.Amount - p.RefundedAmount),
                bInv.Where(i => i.Balance > 0).Sum(i => i.Balance),
                bInv.Where(i => i.Balance > 0 && i.DueDate < branchToday).Select(i => i.StudentId).Distinct().Count(),
                Rate(bAtt.Count(a => Attended.Contains(a.Status)), bAtt.Count(a => Counted.Contains(a.Status))),
                bLeads.Count,
                bLeads.Count(l => l.Status == LeadStatus.Converted),
                Rate(bLeads.Count(l => l.Status == LeadStatus.Converted), bLeads.Count));
        }).ToList();

        var convertedLeads = periodLeads.Count(l => l.Status == LeadStatus.Converted);
        var attendedTrialLeads = periodTrials
            .Where(t => t.AttendanceStatus == TrialAttendanceStatus.Attended)
            .Select(t => t.LeadId).Distinct().ToList();

        return (new OrganizationDashboardDto(
            start, end,
            students.Count,
            students.Count(s => s.Status == StudentStatus.Active),
            students.Count(s => s.Status == StudentStatus.Frozen),
            students.Count(s => s.Status == StudentStatus.Debt),
            students.Count(s => s.JoinedDate >= start && s.JoinedDate <= end),
            groups.Count(g => g.Status == GroupStatus.Active),
            lessonsToday.Count,
            teacherCount,
            payments.Sum(p => p.Amount - p.RefundedAmount),
            invoices.Where(i => i.PeriodStart >= start && i.PeriodStart <= end).Sum(i => i.Net),
            invoices.Where(i => i.Balance > 0).Sum(i => i.Balance),
            invoices.Where(i => i.Balance > 0 && i.DueDate < BranchClock.Today(timezone[i.BranchId], now))
                .Select(i => i.StudentId).Distinct().Count(),
            periodLeads.Count,
            periodTrials.Select(t => t.LeadId).Distinct().Count(),
            attendedTrialLeads.Count,
            convertedLeads,
            Rate(attendedTrialLeads.Count(convertedIds.Contains), attendedTrialLeads.Count),
            Rate(convertedLeads, periodLeads.Count),
            Rate(periodAttendance.Count(a => Attended.Contains(a.Status)), periodAttendance.Count(a => Counted.Contains(a.Status))),
            retention,
            openCases,
            pendingReviews,
            branchKpis,
            payments.Sum(p => p.RefundedAmount),
            invoices.Where(i => i.PeriodStart >= start && i.PeriodStart <= end).Sum(i => i.Discount),
            ai.Requests, ai.CostUsd, ai.ActiveStudents), null);
    }

    // Branch ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One branch's own dashboard: the organization dashboard cut to it, plus what only a branch view has room for — the learning
    /// results (Academic), a league table of its teachers, and the lead funnel. One request feeds every tab of the screen. The work is kept
    /// for a few minutes (<see cref="BranchDashboardCache"/>): it is the heaviest read in the product and nobody needs it to the second.</summary>
    [HttpGet("branch/{id:guid}")]
    [Authorize(Policy = "dashboard.view")]
    public async Task<ActionResult<BranchDashboardDto>> Branch(
        Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope is not (ScopeType.AllOrganization or ScopeType.MyBranches)) return Forbid();

        var branch = await db.Branches.Where(b => b.Id == id && b.OrganizationId == scope.OrganizationId)
            .Select(b => new { b.Id, b.Name, b.Address, b.Timezone }).FirstOrDefaultAsync(ct);
        if (branch is null) return NotFound();
        if (scope.AllowedBranchIds([id])?.Contains(id) != true) return Forbid();

        var period = ResolvePeriod(from, to, out var problem);
        if (period is null) return BadRequest(new { error = problem });
        var (start, end) = period.Value;

        // The payload is the same for everyone who may read the branch; what a caller may not see is cut out afterwards, so a cached
        // copy can never hand the money or the teachers to someone without the permission.
        var full = await branchCache.GetOrCreateAsync($"branch:{scope.OrganizationId}:{id}:{start}:{end}", async () =>
        {
            var (overview, failure) = await BuildOrganizationAsync(scope, start, end, id, ct);
            if (overview is null) throw new InvalidOperationException($"Branch dashboard could not be built: {failure}");

            var academic = await BranchQueries.AcademicAsync(db, id, branch.Timezone, start, end, ct);
            var teachers = await BranchQueries.TeachersAsync(db, id, branch.Timezone, start, end, ct);
            var crm = await LeadFunnelQueries.ForBranchesAsync(db, [(id, branch.Timezone)], start, end, ct);
            return new BranchDashboardDto(id, branch.Name, branch.Address, branch.Timezone, start, end, overview, true, academic, teachers, crm);
        });

        var canSeeFinance = await permissions.ResolveAsync(scope.UserId, "payments.view", ct) is not null;
        var canSeeTeachers = await permissions.ResolveAsync(scope.UserId, "dashboard.teachers.view", ct) is not null;
        var canSeeAi = await permissions.ResolveAsync(scope.UserId, "ai.usage.view", ct) is not null;
        return Ok(Redacted(full, canSeeFinance, canSeeTeachers, canSeeAi));
    }

    /// <summary>The branch dashboard as one caller may see it: no money without <c>payments.view</c>, no AI figures without
    /// <c>ai.usage.view</c>, no teacher league table without <c>dashboard.teachers.view</c>.</summary>
    private static BranchDashboardDto Redacted(BranchDashboardDto full, bool finance, bool teachers, bool ai)
    {
        var overview = finance ? full.Overview : full.Overview with
        {
            Collected = 0, ExpectedRevenue = 0, Outstanding = 0, Debtors = 0, RefundedAmount = 0, DiscountAmount = 0,
            Branches = full.Overview.Branches.Select(b => b with { Collected = 0, Outstanding = 0, Debtors = 0 }).ToList(),
        };
        return full with { Overview = ai ? overview : WithoutAi(overview), FinanceVisible = finance, Teachers = teachers ? full.Teachers : null };
    }

    // Teacher ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What a teacher opens the day with: the lessons they are about to give, the work waiting on their
    /// review, and the students who have started to slip.</summary>
    [HttpGet("teacher")]
    [Authorize(Policy = "dashboard.view")]
    public async Task<ActionResult<TeacherDashboardDto>> Teacher(CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var now = DateTimeOffset.UtcNow;

        var myGroupIds = await db.GroupStaff
            .Where(gs => gs.UserId == scope.UserId)
            .Select(gs => gs.GroupId)
            .Distinct()
            .ToListAsync(ct);

        // A teaching administrator holds a wide scope but still only teaches their own lessons; the lessons they
        // are personally down for is the honest answer for both.
        var lessonRows = await LessonRowsAsync(
            db.Lessons.Held().Where(l => (l.TeacherId == scope.UserId || myGroupIds.Contains(l.GroupId))
                && l.StartsAt >= now.AddDays(-2) && l.StartsAt <= now.AddDays(3)), ct);

        var studentIds = await db.Enrollments
            .Where(e => myGroupIds.Contains(e.GroupId) && e.Status == EnrollmentStatus.Active)
            .Select(e => e.StudentId).Distinct().ToListAsync(ct);

        var pendingHomework = await (
            from s in db.HomeworkSubmissions
            join h in db.Homework on s.HomeworkId equals h.Id
            join l in db.Lessons on h.LessonId equals l.Id
            where s.Score == null && (l.TeacherId == scope.UserId || myGroupIds.Contains(l.GroupId))
            select s.Id).CountAsync(ct);

        var pendingWriting = await db.WritingEvaluations
            .Where(e => studentIds.Contains(e.StudentId)
                && e.Status == WritingEvaluationStatus.Completed && e.TeacherBand == null)
            .CountAsync(ct);

        // Finished Speaking practice staff may read (the student was told) that no teacher has corrected or confirmed yet.
        var pendingSpeaking = await db.SpeakingSessions
            .Where(s => studentIds.Contains(s.StudentId) && s.SharedWithStaff
                && s.Status == SpeakingSessionStatus.Completed && s.OverriddenAt == null)
            .CountAsync(ct);

        // A lesson that has finished with nobody marked present or absent — the single most common thing a
        // teacher forgets, and the thing every downstream attendance number depends on.
        var finishedLessonIds = await db.Lessons.Held()
            .Where(l => (l.TeacherId == scope.UserId || myGroupIds.Contains(l.GroupId))
                && l.EndsAt < now && l.EndsAt > now.AddDays(-14))
            .Select(l => l.Id).ToListAsync(ct);
        var markedLessons = await db.Attendance
            .Where(a => finishedLessonIds.Contains(a.LessonId))
            .Select(a => a.LessonId).Distinct().CountAsync(ct);

        var attendance = await (
            from a in db.Attendance
            join l in db.Lessons.Held() on a.LessonId equals l.Id
            where myGroupIds.Contains(l.GroupId) && l.StartsAt > now.AddDays(-30)
            select new { a.Status, a.StudentId }).ToListAsync(ct);

        // Risk: three or more absences in the last month, or an open support case. Both facts are already in the
        // data — the point of putting them here is that otherwise nobody goes looking for them.
        var absencesByStudent = attendance
            .Where(a => a.Status == AttendanceStatus.Absent)
            .GroupBy(a => a.StudentId)
            .ToDictionary(g => g.Key, g => g.Count());

        var names = await (
            from s in db.Students
            join u in db.Users on s.UserId equals u.Id
            where studentIds.Contains(s.Id)
            select new { s.Id, u.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        var groupsOfStudent = await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            where studentIds.Contains(e.StudentId) && myGroupIds.Contains(e.GroupId)
            select new { e.StudentId, g.Name }).ToListAsync(ct);
        var groupName = groupsOfStudent
            .GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Distinct()));

        var flaggedStudents = await db.SupportCases
            .Where(c => studentIds.Contains(c.StudentId)
                && (c.Status == SupportCaseStatus.Recommended || c.Status == SupportCaseStatus.Assigned))
            .Select(c => c.StudentId).Distinct().ToListAsync(ct);

        var risks = studentIds
            .Select(id =>
            {
                var absences = absencesByStudent.GetValueOrDefault(id);
                var flagged = flaggedStudents.Contains(id);
                if (absences < 3 && !flagged) return null;
                var reasons = new List<string>();
                if (absences >= 3) reasons.Add($"oxirgi oyda {absences} ta darsga kelmadi");
                if (flagged) reasons.Add("support holati ochiq");
                return new RiskStudentDto(id, names.GetValueOrDefault(id, "—"),
                    groupName.GetValueOrDefault(id, "—"), string.Join("; ", reasons), absences >= 5 ? 2 : 1);
            })
            .OfType<RiskStudentDto>()
            .OrderByDescending(r => r.Severity)
            .ThenBy(r => r.FullName)
            .Take(10)
            .ToList();

        var unread = await db.Notifications.CountAsync(n => n.UserId == scope.UserId && n.ReadAt == null, ct);

        return Ok(new TeacherDashboardDto(
            OnLocalDay(lessonRows, now),
            OnLocalDay(lessonRows, now, 1),
            myGroupIds.Count,
            studentIds.Count,
            pendingHomework,
            pendingWriting,
            finishedLessonIds.Count - markedLessons,
            Rate(attendance.Count(a => Attended.Contains(a.Status)), attendance.Count(a => Counted.Contains(a.Status))),
            risks,
            unread,
            pendingSpeaking));
    }

    // Student ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The student's own page. Before this existed a student landed on the student list and saw a table
    /// of one row — technically correct and useless.</summary>
    [HttpGet("student")]
    [Authorize(Policy = "dashboard.view")]
    public async Task<ActionResult<StudentDashboardDto>> Student(CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var studentId = await db.Students
            .Where(s => s.UserId == scope.UserId)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (studentId is null) return NotFound(new { error = "Sizga o'quvchi kartasi biriktirilmagan." });

        var dto = await BuildStudentAsync(studentId.Value, ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    // Parent ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One card per child. A parent's whole relationship with the centre is "is my child going, is the
    /// work done, do I owe anything" — that is the entire screen.</summary>
    [HttpGet("parent")]
    [Authorize(Policy = "dashboard.view")]
    public async Task<ActionResult<ParentDashboardDto>> Parent(CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var childIds = await (
            from sp in db.StudentParents
            join p in db.Parents on sp.ParentId equals p.Id
            where p.UserId == scope.UserId
            select sp.StudentId).Distinct().ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var children = new List<ParentChildDto>();
        foreach (var id in childIds)
        {
            var s = await BuildStudentAsync(id, ct);
            if (s is null) continue;
            children.Add(new ParentChildDto(
                s.StudentId, s.FullName, s.CurrentLevel, s.Status,
                s.Today,
                s.Upcoming.FirstOrDefault(l => l.StartsAt > now),
                s.AttendanceRate, s.LessonsAttended, s.LessonsHeld,
                s.PendingHomework.Count,
                s.Money,
                s.LastExamOverall, s.LastExamName, s.LastExamDate));
        }

        return Ok(new ParentDashboardDto(children.OrderBy(c => c.FullName).ToList()));
    }

    /// <summary>Everything one student's card needs. Shared by the student's own dashboard and the parent's, so a
    /// parent can never be shown a number the student is not shown.</summary>
    private async Task<StudentDashboardDto?> BuildStudentAsync(Guid studentId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var head = await (
            from s in db.Students
            join u in db.Users on s.UserId equals u.Id
            where s.Id == studentId
            select new { s.Id, u.FullName, s.CurrentLevel, s.Status }).FirstOrDefaultAsync(ct);
        if (head is null) return null;

        var groupIds = await db.Enrollments
            .Where(e => e.StudentId == studentId && e.Status == EnrollmentStatus.Active)
            .Select(e => e.GroupId).ToListAsync(ct);

        var lessonRows = await LessonRowsAsync(
            db.Lessons.Held().Where(l => groupIds.Contains(l.GroupId)
                && l.StartsAt >= now.AddDays(-2) && l.StartsAt <= now.AddDays(8)), ct);

        var upcoming = lessonRows.Select(r => r.Dto)
            .Where(l => l.EndsAt >= now).OrderBy(l => l.StartsAt).Take(5).ToList();

        var submitted = await db.HomeworkSubmissions
            .Where(h => h.StudentId == studentId).Select(h => h.HomeworkId).ToListAsync(ct);

        var pending = await (
            from h in db.Homework.AssignedTo(db, studentId) // an individual task given to a classmate is not this student's to hand in
            join l in db.Lessons on h.LessonId equals l.Id
            join g in db.Groups on l.GroupId equals g.Id
            where groupIds.Contains(l.GroupId) && !submitted.Contains(h.Id) && h.Deadline > now.AddDays(-30)
            orderby h.Deadline
            select new PendingHomeworkDto(h.Id, h.LessonId, h.Title, g.Name, h.Deadline, h.Deadline < now))
            .ToListAsync(ct);

        var attendance = await db.Attendance
            .Where(a => a.StudentId == studentId).Select(a => a.Status).ToListAsync(ct);
        var attended = attendance.Count(a => Attended.Contains(a));
        var held = attendance.Count(a => Counted.Contains(a));

        var invoices = await db.Invoices
            .Where(i => i.StudentId == studentId && i.Status != InvoiceStatus.Cancelled && i.Status != InvoiceStatus.Split)
            .Select(i => new { i.DueDate, Balance = i.Amount - i.DiscountAmount - i.CreditedAmount - i.PaidAmount })
            .ToListAsync(ct);
        var unpaid = invoices.Where(i => i.Balance > 0).ToList();
        var utcToday = DateOnly.FromDateTime(now.UtcDateTime);
        var money = new MoneyDueDto(
            unpaid.Sum(i => i.Balance),
            unpaid.Count == 0 ? null : unpaid.Min(i => i.DueDate),
            unpaid.Exists(i => i.DueDate < utcToday));

        var writing = await db.WritingEvaluations
            .Where(e => e.StudentId == studentId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(5)
            .Select(e => new AiResultDto("writing", e.TaskPrompt,
                e.TeacherBand ?? e.EstimatedBand, e.Status.ToString(), e.CreatedAt, "/ai/writing"))
            .ToListAsync(ct);

        var speaking = await db.SpeakingSessions
            .Where(s => s.StudentId == studentId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .Select(s => new AiResultDto("speaking", s.Topic, s.EstimatedBand, s.Status.ToString(), s.CreatedAt, "/ai/speaking"))
            .ToListAsync(ct);

        var lastExam = await (
            from r in db.ExamResults
            join e in db.Exams on r.ExamId equals e.Id
            where r.StudentId == studentId
            orderby e.Date descending
            select new { e.Name, e.Date, r.Overall }).FirstOrDefaultAsync(ct);

        return new StudentDashboardDto(
            head.Id, head.FullName, head.CurrentLevel, head.Status,
            OnLocalDay(lessonRows, now),
            upcoming,
            pending,
            Rate(attended, held), attended, held,
            money,
            writing.Concat(speaking).OrderByDescending(a => a.At).Take(5).ToList(),
            lastExam?.Overall, lastExam?.Name, lastExam?.Date);
    }
}
