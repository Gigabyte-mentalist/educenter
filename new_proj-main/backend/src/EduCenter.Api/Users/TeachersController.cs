using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Users;

public record TeacherGroupDto(Guid GroupId, string GroupName, GroupStaffRole Role, int StudentCount);
public record TeacherStudentDto(Guid StudentId, string FullName, string StudentNumber, Guid GroupId, string GroupName);
/// <param name="PhotoVersion">Changes with every new picture (null: none) — the address of the picture carries it, as on a student's profile.</param>
public record TeacherOverviewDto(Guid UserId, string FullName, string Email, List<TeacherGroupDto> Groups, int TotalStudents,
    List<TeacherStudentDto> Students, string? PhotoVersion = null);
/// <param name="CanOpen">Whether the caller may open this teacher's profile. A teacher sees their colleagues in the list
/// (who co-staffs their groups) but only opens their own page — the UI turns the row into a link only when this is set.</param>
/// <param name="Specialization">From the portfolio; only filled in for a caller who may read portfolios, and for rows they may open.</param>
/// <param name="Headline">The teacher's confirmed exam scores, newest three — same visibility as <paramref name="Specialization"/>.</param>
public record TeacherListDto(Guid Id, string FullName, string Email, UserStatus Status, bool CanOpen,
    string? Specialization = null, List<HeadlineChipDto>? Headline = null);

/// <summary>Percentages are 0-100, matching StudentOverviewDto's convention (this page sits next to a
/// student profile, not the org dashboard, which uses a 0-1 fraction). AverageExamScore is null when the
/// teacher's groups have no recorded exam results yet, not zero.</summary>
public record TeacherPerformanceDto(
    int ActiveStudents, int GroupsCount, double AttendanceRate, double HomeworkCompletionRate, decimal? AverageExamScore);

/// <summary>The Overview tab. Branches are the ones the caller may see (a branch administrator sees the branches they share).
/// Specialization and Headline come from the portfolio and are left empty for a caller who may not read it.</summary>
public record TeacherSummaryDto(
    Guid UserId, string FullName, string Email, string Phone, UserStatus Status,
    List<string> Branches, DateTimeOffset JoinedAt, int GroupsCount, int ActiveStudents,
    string? Specialization = null, List<HeadlineChipDto>? Headline = null);

/// <summary>One colleague on a group — either the assistant of this teacher's group or the main teacher of the group they assist.</summary>
public record TeacherStaffLinkDto(Guid UserId, string FullName, Guid GroupId, string GroupName);

/// <param name="SupportTeachers">Assistants in the groups this teacher leads.</param>
/// <param name="AssistsIn">Main teachers of the groups this teacher assists (empty unless they are a support teacher somewhere).</param>
public record TeacherSupportDto(List<TeacherStaffLinkDto> SupportTeachers, List<TeacherStaffLinkDto> AssistsIn);

/// <summary>Teacher 360° (docs/00_TZ.md §6) — groups the caller can see this teacher through, plus a
/// per-group student count. KPI scoring itself is Phase 6 scope; this is the roster/groups view.</summary>
[ApiController]
[Route("api/teachers")]
public class TeachersController(AppDbContext db, PermissionResolver permissions) : ControllerBase
{
    private async Task<bool> CanReadPortfoliosAsync(ResolvedScope scope, CancellationToken ct) =>
        await permissions.ResolveAsync(scope.UserId, "teacher.portfolio.view", ct) is not null;

    /// <summary>The confirmed exam scores of these teachers, newest first, keyed by teacher — one query however many teachers.</summary>
    private async Task<ILookup<Guid, HeadlineChipDto>> HeadlinesAsync(List<Guid> teacherIds, CancellationToken ct)
    {
        var credentials = await db.TeacherCredentials
            .Where(c => teacherIds.Contains(c.UserId) && c.Type == CredentialType.ExamScore && c.IsVerified).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return credentials.GroupBy(c => c.UserId)
            .SelectMany(g => TeacherPortfolioController.HeadlineOf(g, today).Select(chip => (g.Key, chip)))
            .ToLookup(x => x.Key, x => x.chip);
    }

    [HttpGet]
    [Authorize(Policy = "users.view")]
    public async Task<ActionResult<List<TeacherListDto>>> List(CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var query = db.TeacherUsers().Where(u => u.OrganizationId == scope.OrganizationId);
        query = scope.Scope switch
        {
            ScopeType.AllOrganization => query,
            ScopeType.MyBranches => query.Where(u => u.Branches.Any(b => scope.BranchIds.Contains(b.BranchId))),
            // "My groups" means colleagues, not just me: everyone who co-staffs at least one of the
            // groups the caller also staffs. Always includes the caller even if they staff no group yet.
            ScopeType.MyGroups or ScopeType.AssignedGroups => query.Where(u =>
                u.Id == scope.UserId ||
                db.GroupStaff.Any(gs => gs.UserId == scope.UserId &&
                    db.GroupStaff.Any(peer => peer.GroupId == gs.GroupId && peer.UserId == u.Id))),
            ScopeType.Self => query.Where(u => u.Id == scope.UserId),
            _ => query.Where(u => false)
        };
        var hasBroadAccess = scope.HasBroadAccess();
        var callerId = scope.UserId;
        var rows = await query.OrderBy(u => u.FullName)
            .Select(u => new TeacherListDto(u.Id, u.FullName, u.Email, u.Status, hasBroadAccess || u.Id == callerId))
            .ToListAsync(ct);

        // A colleague's specialization and certificates are not the caller's to see, so only the rows they may open carry them.
        if (!await CanReadPortfoliosAsync(scope, ct)) return Ok(rows);
        var openIds = rows.Where(r => r.CanOpen).Select(r => r.Id).ToList();
        var specializations = await db.TeacherProfiles.Where(p => openIds.Contains(p.UserId) && p.Specialization != null)
            .ToDictionaryAsync(p => p.UserId, p => p.Specialization, ct);
        var headlines = await HeadlinesAsync(openIds, ct);
        return Ok(rows.Select(r => r.CanOpen
            ? r with { Specialization = specializations.GetValueOrDefault(r.Id), Headline = headlines[r.Id].ToList() }
            : r).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "users.view")]
    public async Task<ActionResult<TeacherOverviewDto>> GetById(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await AccessFailureAsync(scope, id, ct) is { } failure) return failure;

        var teacher = await db.TeacherUsers().FirstAsync(u => u.Id == id, ct);

        // A self-view shows the full group list regardless of scope shape; a branch administrator only the groups in their branches.
        var visible = await db.VisibleGroupsAsync(scope, id, ct);

        var groupIds = visible.Select(g => g.GroupId).ToList();
        var students = await (
            from e in db.Enrollments
            join s in db.Students on e.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            join g in db.Groups on e.GroupId equals g.Id
            where groupIds.Contains(e.GroupId) && e.Status == Modules.Students.Domain.EnrollmentStatus.Active
                && s.OrganizationId == scope.OrganizationId && u.OrganizationId == scope.OrganizationId
            orderby u.FullName
            select new TeacherStudentDto(s.Id, u.FullName, s.StudentNumber, g.Id, g.Name)
        ).ToListAsync(ct);
        var counts = students.GroupBy(s => s.GroupId).ToDictionary(g => g.Key, g => g.Count());
        var groups = visible.Select(g => new TeacherGroupDto(g.GroupId, g.GroupName, g.Role, counts.GetValueOrDefault(g.GroupId))).ToList();
        return Ok(new TeacherOverviewDto(teacher.Id, teacher.FullName, teacher.Email, groups,
            students.Select(s => s.StudentId).Distinct().Count(), students,
            teacher.PhotoKey is { } key ? UserPhotoStorage.VersionOf(key) : null));
    }

    /// <summary>Teacher 360° Performance tab (audit §3.6): a read-only snapshot built entirely from data
    /// the product already tracks — attendance, homework, exam results — rather than the TZ's full KPI
    /// model (editable weights, punctuality, feedback), which needs infrastructure this app doesn't have
    /// yet and stays Phase 6 scope (see the class doc-comment on this controller).</summary>
    [HttpGet("{id:guid}/performance")]
    [Authorize(Policy = "users.view")]
    public async Task<ActionResult<TeacherPerformanceDto>> Performance(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await AccessFailureAsync(scope, id, ct) is { } failure) return failure;

        var groups = await db.VisibleGroupsAsync(scope, id, ct);
        return Ok(await TeacherWorkloadQueries.PerformanceAsync(db, groups, ct));
    }

    /// <summary>The Overview tab: who this teacher is at a glance.</summary>
    [HttpGet("{id:guid}/summary")]
    [Authorize(Policy = "users.view")]
    public async Task<ActionResult<TeacherSummaryDto>> Summary(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await AccessFailureAsync(scope, id, ct) is { } failure) return failure;

        var teacher = await db.Users.Where(u => u.Id == id)
            .Select(u => new { u.FullName, u.Email, u.Phone, u.Status, u.CreatedAt }).FirstAsync(ct);

        // A branch administrator sees the branches they share with the teacher, not the teacher's whole footprint.
        var branchQuery = db.UserBranches.Where(ub => ub.UserId == id);
        if (scope.Scope == ScopeType.MyBranches) branchQuery = branchQuery.Where(ub => scope.BranchIds.Contains(ub.BranchId));
        var branches = await (from ub in branchQuery
                              join b in db.Branches on ub.BranchId equals b.Id
                              orderby b.Name
                              select b.Name).ToListAsync(ct);

        var groups = await db.VisibleGroupsAsync(scope, id, ct);
        var groupIds = groups.Select(g => g.GroupId).ToList();
        var students = await db.Enrollments
            .Where(e => groupIds.Contains(e.GroupId) && e.Status == Modules.Students.Domain.EnrollmentStatus.Active)
            .Select(e => e.StudentId).Distinct().CountAsync(ct);

        var summary = new TeacherSummaryDto(
            id, teacher.FullName, teacher.Email, teacher.Phone, teacher.Status, branches, teacher.CreatedAt, groups.Count, students);
        if (!await CanReadPortfoliosAsync(scope, ct)) return Ok(summary);

        var specialization = await db.TeacherProfiles.Where(p => p.UserId == id).Select(p => p.Specialization).FirstOrDefaultAsync(ct);
        return Ok(summary with { Specialization = specialization, Headline = (await HeadlinesAsync([id], ct))[id].ToList() });
    }

    /// <summary>The Attendance, Homework and Exam Results tabs in one response (default: the last 30 days).</summary>
    [HttpGet("{id:guid}/workload")]
    [Authorize(Policy = "users.view")]
    public async Task<ActionResult<TeacherWorkloadDto>> Workload(
        Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var period = TeacherPeriod.Resolve(from, to, DateTimeOffset.UtcNow, out var error);
        if (period is null) return BadRequest(new { error });
        if (await AccessFailureAsync(scope, id, ct) is { } failure) return failure;

        var groups = await db.VisibleGroupsAsync(scope, id, ct);
        return Ok(await TeacherWorkloadQueries.WorkloadAsync(db, groups, period, ct));
    }

    /// <summary>Who assists this teacher in their groups — and, if they are themselves a support teacher, whose groups they assist.</summary>
    [HttpGet("{id:guid}/support-teachers")]
    [Authorize(Policy = "users.view")]
    public async Task<ActionResult<TeacherSupportDto>> SupportTeachers(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (await AccessFailureAsync(scope, id, ct) is { } failure) return failure;

        var groups = await db.VisibleGroupsAsync(scope, id, ct);
        var mainGroupIds = groups.Where(g => g.Role == GroupStaffRole.MainTeacher).Select(g => g.GroupId).ToList();
        var supportGroupIds = groups.Where(g => g.Role == GroupStaffRole.SupportTeacher).Select(g => g.GroupId).ToList();

        var assistants = await (
            from gs in db.GroupStaff
            join u in db.Users on gs.UserId equals u.Id
            join g in db.Groups on gs.GroupId equals g.Id
            where mainGroupIds.Contains(gs.GroupId) && gs.Role == GroupStaffRole.SupportTeacher && gs.UserId != id
            orderby u.FullName, g.Name
            select new TeacherStaffLinkDto(u.Id, u.FullName, g.Id, g.Name)).ToListAsync(ct);

        var assists = await (
            from gs in db.GroupStaff
            join u in db.Users on gs.UserId equals u.Id
            join g in db.Groups on gs.GroupId equals g.Id
            where supportGroupIds.Contains(gs.GroupId) && gs.Role == GroupStaffRole.MainTeacher && gs.UserId != id
            orderby u.FullName, g.Name
            select new TeacherStaffLinkDto(u.Id, u.FullName, g.Id, g.Name)).ToListAsync(ct);

        return Ok(new TeacherSupportDto(assistants, assists));
    }

    /// <summary>Null when the caller may open this teacher's page; otherwise the response that says why not.</summary>
    private async Task<ActionResult?> AccessFailureAsync(ResolvedScope scope, Guid teacherId, CancellationToken ct)
    {
        var access = await db.CheckTeacherAccessAsync(scope, teacherId, ct);
        if (access == TeacherAccessResult.NotFound) return NotFound();
        return access == TeacherAccessResult.Forbidden ? Forbid() : null;
    }
}
