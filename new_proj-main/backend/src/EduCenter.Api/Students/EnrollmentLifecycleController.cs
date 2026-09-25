using System.Text.Json;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Students;

public record TransferRequest(Guid FromGroupId, Guid ToGroupId, string Reason);
public record FreezeRequest(string Reason, DateOnly? ResumeOn);
public record ResumeRequest(Guid? GroupId);

public record TransferTargetDto(Guid GroupId, string GroupName, Guid BranchId, string BranchName,
    string CourseName, int Capacity, int Enrolled, int FreeSeats, string? MainTeacher);

public record EnrollmentMoveDto(Guid StudentId, StudentStatus Status, List<EnrollmentSummaryDto> Enrollments);

/// <summary>
/// The two moves a language centre makes every week and the product could not express (docs/00_TZ.md §20–21):
/// a student changes group — or branch — mid-course, and a student pauses for a month and comes back.
/// <para>
/// Both are enrollment-level, not student-level: a student may sit in two groups and freeze only one. The
/// student's own status follows from what is left — still Active while any enrollment is, Frozen once none is.
/// Billing needs no special case: <c>BillingRules</c> already only invoices Active enrollments, so a frozen
/// enrollment stops being billed the moment it is frozen, and starts again when it resumes.
/// </para>
/// </summary>
public record EnrollGroupRequest(Guid GroupId);
public record DropEnrollmentRequest(string Reason);

[ApiController]
[Route("api/students")]
public class EnrollmentLifecycleController(
    AppDbContext db,
    NotificationDispatcher notifications,
    NotificationRecipients recipients,
    BillingRules billing,
    ILogger<EnrollmentLifecycleController> logger) : ControllerBase
{
    /// <summary>Adds a second (or third…) group without disturbing any enrollment the student already has —
    /// unlike <see cref="Transfer"/>, nothing here ends. A student who is <see cref="StudentStatus.Dropped"/> or
    /// <see cref="StudentStatus.Graduated"/> comes back to <see cref="StudentStatus.Active"/> the same way any
    /// other status recompute would read it: this enrollment makes one of theirs Active again.</summary>
    [HttpPost("{id:guid}/enrollments")]
    [Authorize(Policy = "student.enroll")]
    public async Task<ActionResult<EnrollmentMoveDto>> Enroll(Guid id, EnrollGroupRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();

        var branchIds = await BranchIdsAsync(scope, ct);
        if (branchIds is null) return Forbid();

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId, ct);
        if (student is null) return NotFound();

        var admission = await EnrollmentRules.CheckAsync(db, scope.OrganizationId, request.GroupId, branchIds, id, ct);
        switch (admission.Problem)
        {
            case null: break;
            case AdmissionProblem.NotFound: return NotFound(new { error = admission.Message });
            case AdmissionProblem.OutOfScope: return Forbid();
            default: return Conflict(new { error = admission.Message });
        }
        var group = admission.Group!;

        var priorEnrollments = await db.Enrollments.Where(e => e.StudentId == id).ToListAsync(ct);

        var enrollment = new Enrollment(scope.OrganizationId, id, group.Id);
        db.Enrollments.Add(enrollment);
        student.SetStatus(StudentStatusRules.Recompute(student, [.. priorEnrollments, enrollment]));

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, id, TimelineEventType.Enrolled,
            JsonSerializer.Serialize(new { groupId = group.Id, groupName = group.Name, byUserId = scope.UserId })));

        var studentName = await recipients.StudentNameAsync(id);
        await notifications.NotifyAsync(scope.OrganizationId, await recipients.StudentAndParentsAsync(id),
            new NotificationMessage(NotificationType.Enrolled, "Yangi guruhga yozildi",
                $"{studentName} {group.Name} guruhiga yozildi.", "/dashboard"), ct);

        await db.SaveChangesAsync(ct);

        // Same call as registration: the conversion/enrollment is already committed, so a billing hiccup must
        // not turn it into an error — the sweep issues the first invoice later in that case.
        try
        {
            await billing.IssueDueInvoicesAsync(DateTimeOffset.UtcNow, enrollmentId: enrollment.Id, ct: ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "First invoice for enrollment {EnrollmentId} not issued at enrollment; the sweep will retry.", enrollment.Id);
        }

        return Ok(await MoveResultAsync(id, ct));
    }

    /// <summary>Leaves a group without a destination — unlike <see cref="Transfer"/>, nothing takes its place.
    /// Open invoices are left alone, the same call as transfer/freeze: they were raised against a period the
    /// student did attend.</summary>
    [HttpPost("{id:guid}/enrollments/{enrollmentId:guid}/drop")]
    [Authorize(Policy = "student.enroll")]
    public async Task<ActionResult<EnrollmentMoveDto>> Drop(Guid id, Guid enrollmentId, DropEnrollmentRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "Chiqarish sababini yozing." });

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId, ct);
        if (student is null) return NotFound();

        var enrollment = await db.Enrollments.FirstOrDefaultAsync(
            e => e.Id == enrollmentId && e.StudentId == id && e.Status == EnrollmentStatus.Active, ct);
        if (enrollment is null) return NotFound(new { error = "Faol enrollment topilmadi." });

        var groupName = await db.Groups.Where(g => g.Id == enrollment.GroupId).Select(g => g.Name).FirstAsync(ct);
        enrollment.End(EnrollmentStatus.Dropped, request.Reason.Trim());

        var allEnrollments = await db.Enrollments.Where(e => e.StudentId == id).ToListAsync(ct);
        student.SetStatus(StudentStatusRules.Recompute(student, allEnrollments));

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, id, TimelineEventType.EnrollmentDropped,
            JsonSerializer.Serialize(new { groupId = enrollment.GroupId, groupName, reason = request.Reason, byUserId = scope.UserId })));

        var studentName = await recipients.StudentNameAsync(id);
        await notifications.NotifyAsync(scope.OrganizationId, await recipients.StudentAndParentsAsync(id),
            new NotificationMessage(NotificationType.EnrollmentDropped, "Guruhdan chiqarildi",
                $"{studentName} {groupName} guruhidan chiqarildi. Sabab: {request.Reason}", "/dashboard"), ct);

        await db.SaveChangesAsync(ct);
        return Ok(await MoveResultAsync(id, ct));
    }

    /// <summary>Finishes the course: every open enrollment (active or frozen) becomes <see cref="EnrollmentStatus.Completed"/>
    /// and the student reaches the one status nothing else recomputes them out of on its own — re-enrolling is what
    /// brings them back (docs/00_TZ.md §10).</summary>
    [HttpPost("{id:guid}/graduate")]
    [Authorize(Policy = "student.enroll")]
    public async Task<ActionResult<EnrollmentMoveDto>> Graduate(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId, ct);
        if (student is null) return NotFound();

        var open = await db.Enrollments
            .Where(e => e.StudentId == id && (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Frozen))
            .ToListAsync(ct);
        if (open.Count == 0) return BadRequest(new { error = "Bitirish uchun faol yoki muzlatilgan guruh yo'q." });

        foreach (var enrollment in open) enrollment.End(EnrollmentStatus.Completed, null);

        var allEnrollments = await db.Enrollments.Where(e => e.StudentId == id).ToListAsync(ct);
        student.SetStatus(StudentStatusRules.Recompute(student, allEnrollments));

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, id, TimelineEventType.Graduated,
            JsonSerializer.Serialize(new { groups = open.Count, byUserId = scope.UserId })));

        var studentName = await recipients.StudentNameAsync(id);
        await notifications.NotifyAsync(scope.OrganizationId, await recipients.StudentAndParentsAsync(id),
            new NotificationMessage(NotificationType.Graduated, "Kursni bitirdi",
                $"{studentName} kursni muvaffaqiyatli bitirdi!", "/dashboard"), ct);

        await db.SaveChangesAsync(ct);
        return Ok(await MoveResultAsync(id, ct));
    }

    /// <summary>The groups this student could be moved into: inside the caller's scope, still running, and with
    /// a free seat. Offering a full group and failing on submit is a worse experience than not offering it.</summary>
    [HttpGet("{id:guid}/transfer-targets")]
    [Authorize(Policy = "student.transfer")]
    public async Task<ActionResult<List<TransferTargetDto>>> TransferTargets(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();

        var branchIds = await BranchIdsAsync(scope, ct);
        if (branchIds is null) return Forbid();

        var current = await db.Enrollments
            .Where(e => e.StudentId == id && e.Status == EnrollmentStatus.Active)
            .Select(e => e.GroupId).ToListAsync(ct);

        var groups = await (
            from g in db.Groups
            join b in db.Branches on g.BranchId equals b.Id
            join c in db.Courses on g.CourseId equals c.Id
            where branchIds.Contains(g.BranchId)
                && (g.Status == GroupStatus.Active || g.Status == GroupStatus.Forming)
                && !current.Contains(g.Id)
            orderby b.Name, g.Name
            select new
            {
                g.Id, GroupName = g.Name, g.BranchId, BranchName = b.Name, CourseName = c.Name, g.Capacity,
                Enrolled = db.Enrollments.Count(e => e.GroupId == g.Id && e.Status == EnrollmentStatus.Active),
                MainTeacher = (from gs in db.GroupStaff
                               join u in db.Users on gs.UserId equals u.Id
                               where gs.GroupId == g.Id && gs.Role == GroupStaffRole.MainTeacher
                               select u.FullName).FirstOrDefault(),
            }).ToListAsync(ct);

        return Ok(groups
            .Select(g => new TransferTargetDto(g.Id, g.GroupName, g.BranchId, g.BranchName, g.CourseName,
                g.Capacity, g.Enrolled, g.Capacity - g.Enrolled, g.MainTeacher))
            .Where(g => g.FreeSeats > 0)
            .ToList());
    }

    [HttpPost("{id:guid}/transfer")]
    [Authorize(Policy = "student.transfer")]
    public async Task<ActionResult<EnrollmentMoveDto>> Transfer(Guid id, TransferRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "Ko'chirish sababini yozing." });
        if (request.FromGroupId == request.ToGroupId)
            return BadRequest(new { error = "O'quvchi allaqachon shu guruhda." });

        var branchIds = await BranchIdsAsync(scope, ct);
        if (branchIds is null) return Forbid();

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId, ct);
        if (student is null) return NotFound();

        var from = await db.Enrollments.FirstOrDefaultAsync(
            e => e.StudentId == id && e.GroupId == request.FromGroupId && e.Status == EnrollmentStatus.Active, ct);
        if (from is null) return BadRequest(new { error = "O'quvchi bu guruhda faol emas." });

        // Both ends must be inside the caller's scope: a branch administrator may move a student between their
        // own branches, but must not move one out of — or into — a branch they do not hold. The far end is checked
        // by the shared admission rules; the near end is checked here.
        var fromBranch = await db.Groups.Where(g => g.Id == request.FromGroupId).Select(g => g.BranchId).FirstOrDefaultAsync(ct);
        if (!branchIds.Contains(fromBranch)) return Forbid();

        var admission = await EnrollmentRules.CheckAsync(db, scope.OrganizationId, request.ToGroupId, branchIds, id, ct);
        switch (admission.Problem)
        {
            case null: break;
            case AdmissionProblem.NotFound: return NotFound(new { error = admission.Message });
            case AdmissionProblem.OutOfScope: return Forbid();
            default: return BadRequest(new { error = admission.Message });
        }
        var to = admission.Group!;

        var fromName = await db.Groups.Where(g => g.Id == request.FromGroupId).Select(g => g.Name).FirstAsync(ct);
        var priorEnrollments = await db.Enrollments.Where(e => e.StudentId == id).ToListAsync(ct);

        from.End(EnrollmentStatus.Transferred, request.Reason);
        var toEnrollment = new Enrollment(scope.OrganizationId, id, to.Id);
        db.Enrollments.Add(toEnrollment);
        student.SetStatus(StudentStatusRules.Recompute(student, [.. priorEnrollments, toEnrollment]));

        // Open invoices are deliberately left alone. They were raised against a period the student did in fact
        // attend, and silently re-pointing money at another group is the kind of correction a human should make
        // explicitly through the finance screen.
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, id, TimelineEventType.Transferred,
            JsonSerializer.Serialize(new
            {
                fromGroup = fromName, toGroup = to.Name, toBranch = to.BranchName,
                reason = request.Reason, byUserId = scope.UserId,
            })));

        var studentName = await recipients.StudentNameAsync(id);
        await notifications.NotifyAsync(scope.OrganizationId, await recipients.StudentAndParentsAsync(id),
            new NotificationMessage(NotificationType.Transferred, "Guruh o'zgardi",
                $"{studentName} {fromName} guruhidan {to.Name} guruhiga ko'chirildi.", "/dashboard"), ct);

        await db.SaveChangesAsync(ct);
        return Ok(await MoveResultAsync(id, ct));
    }

    [HttpPost("{id:guid}/freeze")]
    [Authorize(Policy = "student.freeze")]
    public async Task<ActionResult<EnrollmentMoveDto>> Freeze(Guid id, FreezeRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { error = "Muzlatish sababini yozing." });

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId, ct);
        if (student is null) return NotFound();

        var active = await db.Enrollments
            .Where(e => e.StudentId == id && e.Status == EnrollmentStatus.Active)
            .ToListAsync(ct);
        if (active.Count == 0) return BadRequest(new { error = "Muzlatish uchun faol guruh yo'q." });

        foreach (var enrollment in active) enrollment.SetStatus(EnrollmentStatus.Frozen);
        var allEnrollments = await db.Enrollments.Where(e => e.StudentId == id).ToListAsync(ct);
        student.SetStatus(StudentStatusRules.Recompute(student, allEnrollments));

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, id, TimelineEventType.CourseFrozen,
            JsonSerializer.Serialize(new
            {
                reason = request.Reason, resumeOn = request.ResumeOn, groups = active.Count, byUserId = scope.UserId,
            })));

        var studentName = await recipients.StudentNameAsync(id);
        var until = request.ResumeOn is { } d ? $" {d:dd.MM.yyyy} gacha." : ".";
        await notifications.NotifyAsync(scope.OrganizationId, await recipients.StudentAndParentsAsync(id),
            new NotificationMessage(NotificationType.CourseFrozen, "O'qish muzlatildi",
                $"{studentName} uchun o'qish vaqtincha to'xtatildi{until} Sabab: {request.Reason}", "/dashboard"), ct);

        await db.SaveChangesAsync(ct);
        return Ok(await MoveResultAsync(id, ct));
    }

    /// <summary>Brings a frozen student back. With no group named every frozen enrollment resumes; with one,
    /// only that group does — a student who froze two courses may only be returning to one.</summary>
    [HttpPost("{id:guid}/resume")]
    [Authorize(Policy = "student.freeze")]
    public async Task<ActionResult<EnrollmentMoveDto>> Resume(Guid id, ResumeRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId, ct);
        if (student is null) return NotFound();

        var frozen = await db.Enrollments
            .Where(e => e.StudentId == id && e.Status == EnrollmentStatus.Frozen
                && (request.GroupId == null || e.GroupId == request.GroupId))
            .ToListAsync(ct);
        if (frozen.Count == 0) return BadRequest(new { error = "Muzlatilgan guruh topilmadi." });

        // A group that closed while the student was away cannot take them back, and saying so here is better
        // than resuming them into a group with no lessons left.
        var closed = await db.Groups
            .Where(g => frozen.Select(f => f.GroupId).Contains(g.Id)
                && (g.Status == GroupStatus.Completed || g.Status == GroupStatus.Cancelled))
            .Select(g => g.Name).ToListAsync(ct);
        if (closed.Count > 0)
            return BadRequest(new { error = $"Bu guruh(lar) yopilgan: {string.Join(", ", closed)}. Avval boshqa guruhga ko'chiring." });

        foreach (var enrollment in frozen) enrollment.SetStatus(EnrollmentStatus.Active);
        var allEnrollments = await db.Enrollments.Where(e => e.StudentId == id).ToListAsync(ct);
        student.SetStatus(StudentStatusRules.Recompute(student, allEnrollments));

        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, id, TimelineEventType.CourseResumed,
            JsonSerializer.Serialize(new { groups = frozen.Count, byUserId = scope.UserId })));

        var studentName = await recipients.StudentNameAsync(id);
        await notifications.NotifyAsync(scope.OrganizationId, await recipients.StudentAndParentsAsync(id),
            new NotificationMessage(NotificationType.CourseFrozen, "O'qish qayta boshlandi",
                $"{studentName} uchun o'qish qayta faollashtirildi.", "/dashboard"), ct);

        await db.SaveChangesAsync(ct);
        return Ok(await MoveResultAsync(id, ct));
    }

    private async Task<EnrollmentMoveDto> MoveResultAsync(Guid id, CancellationToken ct)
    {
        var status = await db.Students.Where(s => s.Id == id).Select(s => s.Status).FirstAsync(ct);
        var enrollments = await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            where e.StudentId == id
            orderby g.Name
            select new EnrollmentSummaryDto(e.Id, g.Id, g.Name, e.Status, e.EnrolledAt)).ToListAsync(ct);
        return new EnrollmentMoveDto(id, status, enrollments);
    }

    /// <summary>The branches the caller may act inside. Only organization- and branch-scoped staff move students;
    /// a teacher's scope gives them no branch at all, which is the right answer for this operation.</summary>
    private async Task<List<Guid>?> BranchIdsAsync(ResolvedScope scope, CancellationToken ct) =>
        scope.Scope switch
        {
            ScopeType.AllOrganization => await db.Branches
                .Where(b => b.OrganizationId == scope.OrganizationId).Select(b => b.Id).ToListAsync(ct),
            ScopeType.MyBranches => scope.BranchIds.ToList(),
            _ => null,
        };
}
