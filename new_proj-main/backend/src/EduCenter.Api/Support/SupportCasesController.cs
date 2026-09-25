using System.Text.Json;
using EduCenter.Api.Authorization;
using EduCenter.Api.Notifications;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Support;

public record SupportCaseDto(
    Guid Id, Guid StudentId, string StudentName, string StudentNumber, Guid GroupId, string GroupName,
    SupportCaseSource Source, string SignalCode, string Reason, SupportCaseStatus Status,
    Guid? ProposedBy, string? ProposedByName, Guid? SupportTeacherId, string? SupportTeacherName,
    string? ResolutionNote, DateTimeOffset CreatedAt, DateTimeOffset? AssignedAt, DateTimeOffset? ClosedAt);

public record ProposeSupportRequest(Guid StudentId, Guid GroupId, string Reason);
public record AssignSupportRequest(Guid SupportTeacherId);
public record CloseSupportCaseRequest(string? Note);
public record SupportCandidateDto(Guid UserId, string FullName, string Email);

/// <summary>Support Needed Engine review flow (docs/00_TZ.md §8, permission matrix §3.9).</summary>
[ApiController]
[Route("api/support-cases")]
public class SupportCasesController(AppDbContext db, NotificationDispatcher notifier, NotificationRecipients recipients) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "support.recommend.view")]
    public async Task<ActionResult<Page<SupportCaseDto>>> List(
        [FromQuery] SupportCaseStatus? status, [FromQuery] SupportCaseStatus[]? statuses, [FromQuery] Guid? studentId,
        [FromQuery] string? search, [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var groups = await scope.ScopedGroupsAsync(db);

        var cases =
            from c in db.SupportCases
            join g in groups on c.GroupId equals g.Id
            where c.OrganizationId == scope.OrganizationId
            select c;

        // "statuses" lets a screen ask for a whole tab at once (closed = dismissed + resolved) without a query per status.
        if (status is not null) cases = cases.Where(c => c.Status == status);
        if (statuses is { Length: > 0 }) cases = cases.WhereIn(c => c.Status, statuses);
        if (studentId is not null) cases = cases.Where(c => c.StudentId == studentId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = PagingExtensions.ContainsPattern(search);
            cases = cases.Where(c => db.Students.Any(s => s.Id == c.StudentId
                && (EF.Functions.ILike(s.StudentNumber, term)
                    || db.Users.Any(u => u.Id == s.UserId && EF.Functions.ILike(u.FullName, term)))));
        }

        // Page the entities first, then project just that page: EF cannot sort or filter over the constructed DTO, and a join after
        // the ORDER BY is free to reorder the rows.
        var page = await cases.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).Select(c => c.Id).ToPageAsync(paging, ct);
        var ids = page.Items.ToList();
        var dtos = (await Project(db.SupportCases.Where(c => ids.Contains(c.Id))).ToListAsync(ct)).ToDictionary(c => c.Id);
        return Ok(new Page<SupportCaseDto>(ids.Where(dtos.ContainsKey).Select(id => dtos[id]).ToList(), page.Total, page.PageNumber, page.PageSize));
    }

    /// <summary>One case, with the same visibility as the list. Universal Search links to a case by id, and with the list paged the
    /// screen can no longer assume the case is somewhere in what it already loaded.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "support.recommend.view")]
    public async Task<ActionResult<SupportCaseDto>> GetById(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var groups = await scope.ScopedGroupsAsync(db);

        var cases =
            from c in db.SupportCases
            join g in groups on c.GroupId equals g.Id
            where c.OrganizationId == scope.OrganizationId && c.Id == id
            select c;

        var dto = await Project(cases).FirstOrDefaultAsync(ct);
        return dto is null ? NotFound() : Ok(dto);
    }

    /// <summary>A teacher or administrator raises a recommendation by hand (Source = Staff).</summary>
    [HttpPost]
    [Authorize(Policy = "support.propose")]
    public async Task<ActionResult<SupportCaseDto>> Propose(ProposeSupportRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
            return BadRequest(new { error = "Sababni kiriting (500 belgigacha)." });

        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == request.GroupId && g.OrganizationId == scope.OrganizationId);
        if (group is null) return NotFound(new { error = "Guruh topilmadi." });
        if (!await scope.CanAccessGroupAsync(db, group.Id, group.BranchId)) return Forbid();

        var enrolled = await db.Enrollments.AnyAsync(e =>
            e.GroupId == group.Id && e.StudentId == request.StudentId && e.Status == EnrollmentStatus.Active);
        if (!enrolled) return BadRequest(new { error = "Student bu guruhda faol o'qimaydi." });

        var hasOpen = await db.SupportCases.AnyAsync(c => c.StudentId == request.StudentId && c.GroupId == group.Id
            && (c.Status == SupportCaseStatus.Recommended || c.Status == SupportCaseStatus.Assigned));
        if (hasOpen) return Conflict(new { error = "Bu student uchun ochiq support tavsiyasi allaqachon mavjud." });

        var supportCase = new SupportCase(scope.OrganizationId, request.StudentId, group.Id, SupportCaseSource.Staff,
            SupportSignalCode.StaffProposal, reason, scope.UserId);
        db.SupportCases.Add(supportCase);

        var proposedFor = await recipients.StudentNameAsync(request.StudentId);
        await notifier.NotifyAsync(scope.OrganizationId, await recipients.BranchAdministratorsAsync(scope.OrganizationId, group.BranchId),
            new NotificationMessage(NotificationType.SupportRecommended, $"Support tavsiyasi: {proposedFor}",
                $"{group.Name} · {reason}", "/support"));
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, request.StudentId,
            TimelineEventType.SupportRecommended,
            JsonSerializer.Serialize(new { signal = SupportSignalCode.StaffProposal, groupId = group.Id, reason })));
        await db.SaveChangesAsync();

        var dto = await Project(db.SupportCases.Where(c => c.Id == supportCase.Id)).FirstAsync();
        return CreatedAtAction(nameof(List), dto);
    }

    /// <summary>Who the case can be assigned to: support teachers staffed on the case's group (TZ §3.5 —
    /// support is a group assignment, so only they can actually reach the student's work).</summary>
    [HttpGet("{id:guid}/candidates")]
    [Authorize(Policy = "support.assign")]
    public async Task<ActionResult<List<SupportCandidateDto>>> Candidates(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (supportCase, error) = await LoadInScopeAsync(scope, id);
        if (error is not null) return error;

        var candidates = await (
            from gs in db.GroupStaff
            join u in db.Users on gs.UserId equals u.Id
            where gs.GroupId == supportCase!.GroupId && gs.Role == GroupStaffRole.SupportTeacher && u.Status == UserStatus.Active
            orderby u.FullName
            select new SupportCandidateDto(u.Id, u.FullName, u.Email)
        ).ToListAsync();

        return Ok(candidates);
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = "support.assign")]
    public async Task<IActionResult> Assign(Guid id, AssignSupportRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (supportCase, error) = await LoadInScopeAsync(scope, id);
        if (error is not null) return error;

        if (supportCase!.Status != SupportCaseStatus.Recommended)
            return Conflict(new { error = "Faqat tavsiya holatidagi case'ga yordamchi o'qituvchi biriktiriladi." });

        var isCandidate = await db.GroupStaff.AnyAsync(gs => gs.GroupId == supportCase.GroupId
            && gs.UserId == request.SupportTeacherId && gs.Role == GroupStaffRole.SupportTeacher);
        if (!isCandidate)
            return BadRequest(new { error = "Tanlangan xodim bu guruhga yordamchi o'qituvchi sifatida biriktirilmagan." });

        supportCase.Assign(request.SupportTeacherId, scope.UserId);

        var assignedStudent = await recipients.StudentNameAsync(supportCase.StudentId);
        var assignedTeacher = await recipients.UserNameAsync(request.SupportTeacherId);
        await notifier.NotifyAsync(scope.OrganizationId, [request.SupportTeacherId],
            new NotificationMessage(NotificationType.SupportAssigned, $"Sizga support biriktirildi: {assignedStudent}",
                supportCase.Reason, "/support"));
        await notifier.NotifyAsync(scope.OrganizationId, await recipients.StudentUserAsync(supportCase.StudentId),
            new NotificationMessage(NotificationType.SupportAssigned, "Sizga yordamchi o'qituvchi biriktirildi",
                $"{assignedTeacher} siz bilan qo'shimcha shug'ullanadi."));

        var teacherName = await db.Users.Where(u => u.Id == request.SupportTeacherId).Select(u => u.FullName).FirstAsync();
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, supportCase.StudentId,
            TimelineEventType.SupportAssigned,
            JsonSerializer.Serialize(new { supportCaseId = supportCase.Id, supportTeacherId = request.SupportTeacherId, supportTeacherName = teacherName })));

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/dismiss")]
    [Authorize(Policy = "support.assign")]
    public async Task<IActionResult> Dismiss(Guid id, CloseSupportCaseRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (supportCase, error) = await LoadInScopeAsync(scope, id);
        if (error is not null) return error;

        if (supportCase!.Status != SupportCaseStatus.Recommended)
            return Conflict(new { error = "Faqat tavsiya holatidagi case rad etiladi." });
        if (request.Note?.Length > 1000)
            return BadRequest(new { error = "Izoh 1000 belgidan oshmasligi kerak." });

        supportCase.Dismiss(scope.UserId, request.Note);
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, supportCase.StudentId,
            TimelineEventType.SupportDismissed, JsonSerializer.Serialize(new { supportCaseId = supportCase.Id })));

        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Closes an assigned case once support has done its job — by the assigned support teacher,
    /// or by an administrator of the group's branch.</summary>
    [HttpPost("{id:guid}/resolve")]
    [Authorize(Policy = "support.recommend.view")]
    public async Task<IActionResult> Resolve(Guid id, CloseSupportCaseRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var (supportCase, error) = await LoadInScopeAsync(scope, id);
        if (error is not null) return error;

        var isAssignee = supportCase!.SupportTeacherId == scope.UserId;
        var isManager = scope.Scope is ScopeType.AllOrganization or ScopeType.MyBranches;
        if (!isAssignee && !isManager) return Forbid();

        if (supportCase.Status != SupportCaseStatus.Assigned)
            return Conflict(new { error = "Faqat biriktirilgan case yopiladi." });
        if (request.Note?.Length > 1000)
            return BadRequest(new { error = "Izoh 1000 belgidan oshmasligi kerak." });

        supportCase.Resolve(scope.UserId, request.Note);
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, supportCase.StudentId,
            TimelineEventType.SupportResolved, JsonSerializer.Serialize(new { supportCaseId = supportCase.Id })));

        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<(SupportCase? Case, ActionResult? Error)> LoadInScopeAsync(ResolvedScope scope, Guid id)
    {
        var supportCase = await db.SupportCases.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == scope.OrganizationId);
        if (supportCase is null) return (null, NotFound());

        // The assigned support teacher keeps reaching their own case even if their group staffing changes later.
        if (supportCase.SupportTeacherId == scope.UserId) return (supportCase, null);

        var branchId = await db.Groups.Where(g => g.Id == supportCase.GroupId).Select(g => g.BranchId).FirstAsync();
        if (!await scope.CanAccessGroupAsync(db, supportCase.GroupId, branchId)) return (null, Forbid());

        return (supportCase, null);
    }

    private IQueryable<SupportCaseDto> Project(IQueryable<SupportCase> cases) =>
        from c in cases
        join s in db.Students on c.StudentId equals s.Id
        join su in db.Users on s.UserId equals su.Id
        join g in db.Groups on c.GroupId equals g.Id
        select new SupportCaseDto(
            c.Id, c.StudentId, su.FullName, s.StudentNumber, c.GroupId, g.Name,
            c.Source, c.SignalCode, c.Reason, c.Status,
            c.ProposedBy, db.Users.Where(u => u.Id == c.ProposedBy).Select(u => u.FullName).FirstOrDefault(),
            c.SupportTeacherId, db.Users.Where(u => u.Id == c.SupportTeacherId).Select(u => u.FullName).FirstOrDefault(),
            c.ResolutionNote, c.CreatedAt, c.AssignedAt, c.ClosedAt);
}
