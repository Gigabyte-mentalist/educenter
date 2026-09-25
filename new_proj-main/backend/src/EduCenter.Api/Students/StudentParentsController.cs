using System.Text.Json;
using EduCenter.Api.Auth;
using EduCenter.Api.Authorization;
using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Students;

public record StudentParentDto(Guid ParentId, Guid UserId, string FullName, string Phone, string Email, ParentRelationship Relationship);

/// <param name="UserId">Mode (a): link an existing account — it must already hold the PARENT role.</param>
/// <param name="FullName">Mode (b): create a new account. Ignored when <paramref name="UserId"/> is set.</param>
public record LinkParentRequest(
    Guid? UserId, string? FullName, string? Phone, string? Email, string? Password,
    ParentRelationship Relationship = ParentRelationship.Guardian);

/// <summary>
/// TZ §24 Parent Portal needs a guardian account linked to a child before there is anything for it to show.
/// Two ways in: pick an existing PARENT-rol account (a second child for someone already signed up) or create one on
/// the spot — either way the link is <c>StudentParent</c>, and a <c>Parent</c> row is reused if the account already
/// has one (a guardian may watch more than one child).
/// </summary>
[ApiController]
[Route("api/students")]
public class StudentParentsController(
    AppDbContext db,
    NotificationDispatcher notifications,
    NotificationRecipients recipients) : ControllerBase
{
    private const int MaxNameLength = 200;
    private const int MaxPhoneLength = 30;

    private static readonly PasswordHasher<User> Hasher = new();

    [HttpGet("{id:guid}/parents")]
    [Authorize(Policy = "students.view")]
    public async Task<ActionResult<List<StudentParentDto>>> List(Guid id, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();

        var parents = await (
            from sp in db.StudentParents
            join p in db.Parents on sp.ParentId equals p.Id
            join u in db.Users on p.UserId equals u.Id
            where sp.StudentId == id
            orderby u.FullName
            select new StudentParentDto(p.Id, u.Id, u.FullName, u.Phone, u.Email, sp.Relationship)
        ).ToListAsync(ct);

        return Ok(parents);
    }

    [HttpPost("{id:guid}/parents")]
    [Authorize(Policy = "parents.manage")]
    public async Task<ActionResult<StudentParentDto>> Link(Guid id, LinkParentRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId, ct);
        if (student is null) return NotFound();

        Guid parentUserId;
        string parentName, parentPhone, parentEmail;
        Parent parent;

        if (request.UserId is { } userId)
        {
            var user = await db.Users.Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.Id == userId && u.OrganizationId == scope.OrganizationId, ct);
            if (user is null) return BadRequest(new { error = "Foydalanuvchi topilmadi." });

            var parentRoleId = await db.Roles.Where(r => r.Code == RoleCode.Parent).Select(r => r.Id).FirstAsync(ct);
            if (!user.Roles.Any(ur => ur.RoleId == parentRoleId))
                return BadRequest(new { error = "Faqat PARENT rolidagi hisobni biriktirish mumkin." });

            var existing = await db.Parents.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
            if (existing is not null && await db.StudentParents.AnyAsync(sp => sp.StudentId == id && sp.ParentId == existing.Id, ct))
                return Conflict(new { error = "Bu ota-ona farzandga allaqachon bog'langan." });

            parent = existing ?? new Parent(scope.OrganizationId, user.Id);
            if (existing is null) db.Parents.Add(parent);

            parentUserId = user.Id;
            parentName = user.FullName;
            parentPhone = user.Phone;
            parentEmail = user.Email;
        }
        else
        {
            var fullName = request.FullName?.Trim() ?? "";
            var phone = request.Phone?.Trim() ?? "";
            var email = request.Email?.Trim().ToLowerInvariant() ?? "";

            if (fullName.Length == 0 || fullName.Length > MaxNameLength)
                return BadRequest(new { error = $"Ism-familiya 1–{MaxNameLength} belgidan iborat bo'lsin." });
            if (phone.Length == 0 || phone.Length > MaxPhoneLength)
                return BadRequest(new { error = $"Telefon 1–{MaxPhoneLength} belgidan iborat bo'lsin." });
            if (!EmailPolicy.IsValid(email)) return BadRequest(new { error = "Yaroqli email kiriting." });
            if (PasswordPolicy.Validate(request.Password) is { } passwordProblem) return BadRequest(new { error = passwordProblem });
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
                return Conflict(new { error = "Bu email bilan foydalanuvchi allaqachon mavjud." });

            var parentRole = await db.Roles.FirstAsync(r => r.Code == RoleCode.Parent, ct);
            var user = new User(scope.OrganizationId, fullName, phone, email, passwordHash: string.Empty);
            user.SetPasswordHash(Hasher.HashPassword(user, request.Password!));
            user.AssignRole(parentRole.Id);
            // Whoever linked this guardian chose the password, not the guardian — S01.05 forces a change on first use.
            user.RequirePasswordChange();

            var branchIds = await (
                from e in db.Enrollments
                join g in db.Groups on e.GroupId equals g.Id
                where e.StudentId == id && e.Status == EnrollmentStatus.Active
                select g.BranchId
            ).Distinct().ToListAsync(ct);
            foreach (var branchId in branchIds) user.AssignBranch(branchId);

            db.Users.Add(user);
            parent = new Parent(scope.OrganizationId, user.Id);
            db.Parents.Add(parent);

            parentUserId = user.Id;
            parentName = user.FullName;
            parentPhone = user.Phone;
            parentEmail = user.Email;
        }

        db.StudentParents.Add(new StudentParent(id, parent.Id, request.Relationship));
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, id, TimelineEventType.ParentLinked,
            JsonSerializer.Serialize(new { parentName, relationship = request.Relationship.ToString(), byUserId = scope.UserId })));

        // NotifyAsync looks the recipient up with its own query against the database, so a brand-new account
        // (mode b) must already be committed — it will not see a User this same request only just Add()-ed.
        await db.SaveChangesAsync(ct);

        var studentName = await recipients.StudentNameAsync(id);
        await notifications.NotifyAsync(scope.OrganizationId, [parentUserId],
            new NotificationMessage(NotificationType.ParentLinked, "Farzand biriktirildi",
                $"Siz {studentName} farzandingiz sifatida biriktirildingiz.", "/dashboard"), ct);

        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { id }, new StudentParentDto(parent.Id, parentUserId, parentName, parentPhone, parentEmail, request.Relationship));
    }

    [HttpDelete("{id:guid}/parents/{parentId:guid}")]
    [Authorize(Policy = "parents.manage")]
    public async Task<IActionResult> Unlink(Guid id, Guid parentId, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();

        var studentExists = await db.Students.AnyAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId, ct);
        if (!studentExists) return NotFound();

        var link = await db.StudentParents.FirstOrDefaultAsync(sp => sp.StudentId == id && sp.ParentId == parentId, ct);
        if (link is null) return NotFound();

        db.StudentParents.Remove(link);
        db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, id, TimelineEventType.ParentUnlinked,
            JsonSerializer.Serialize(new { byUserId = scope.UserId })));

        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
