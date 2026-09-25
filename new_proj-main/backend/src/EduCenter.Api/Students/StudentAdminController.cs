using System.Text.Json;
using EduCenter.Api.Auth;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.Modules.Timeline.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Students;

/// <param name="JoinedDate">When they started. Defaults to today; a centre moving over from another system enters the
/// real date, which is why the past is allowed.</param>
/// <param name="GroupId">The first group. Required unless the caller holds the whole organization — see the controller.</param>
/// <param name="StudentNumber">Left empty, the next STU-yyyyMM-NNNN is issued; a centre that already numbers its
/// students keeps its own.</param>
public record CreateStudentRequest(
    string FullName, string Phone, string Email, string Password,
    DateOnly? JoinedDate, string? CurrentLevel, Guid? GroupId, string? StudentNumber = null);

/// <summary>Every field is optional and a missing one means "leave it". <paramref name="CurrentLevel"/> is the exception
/// that needs a way to say "clear it": an empty string does.</summary>
public record UpdateStudentRequest(
    string? FullName, string? Phone, string? StudentNumber, DateOnly? JoinedDate, string? CurrentLevel);

public record UpdateStudentContactRequest(string? FullName, string? Phone);

/// <summary>
/// Registering and correcting a student directly (docs/00_TZ.md §7). Until now a student existed only as the end of a
/// lead's conversion, so a centre could not enter the people it already teaches. The login <c>User</c>, the
/// <c>Student</c> profile and, when a group is named, the enrollment are written in one save — either all of them
/// exist or none does.
/// <para>
/// A branch-scoped caller must name a group. Branch scope reaches a student only through an enrollment in one of
/// their branches (<see cref="StudentAccess"/>), so a student registered with no group would be invisible to the very
/// person who just registered them. Only a caller holding the whole organization may register someone unplaced.
/// </para>
/// </summary>
[ApiController]
[Route("api/students")]
public class StudentAdminController(AppDbContext db, BillingRules billing, ILogger<StudentAdminController> logger) : ControllerBase
{
    private const int MaxNameLength = 200;
    private const int MaxPhoneLength = 30;
    private const int MaxLevelLength = 50;
    private const int MaxNumberLength = 50;

    private static readonly PasswordHasher<User> Hasher = new();

    [HttpPost]
    [Authorize(Policy = "students.create")]
    public async Task<ActionResult<StudentDto>> Create(CreateStudentRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();

        var fullName = request.FullName?.Trim() ?? "";
        var phone = request.Phone?.Trim() ?? "";
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var level = Clean(request.CurrentLevel);
        var number = Clean(request.StudentNumber);

        if (ProfileProblem(fullName, phone, level) is { } profileProblem) return BadRequest(new { error = profileProblem });
        if (!EmailPolicy.IsValid(email)) return BadRequest(new { error = "Yaroqli email kiriting." });
        if (PasswordPolicy.Validate(request.Password) is { } passwordProblem) return BadRequest(new { error = passwordProblem });
        if (number is { Length: > MaxNumberLength }) return BadRequest(new { error = $"Student raqami {MaxNumberLength} belgidan oshmasin." });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var joined = request.JoinedDate ?? today;
        if (JoinedInFuture(joined, today)) return BadRequest(new { error = "Qo'shilgan sana kelajakda bo'lmasin." });

        var orgBranchIds = await db.Branches
            .Where(b => b.OrganizationId == scope.OrganizationId).Select(b => b.Id).ToListAsync(ct);
        var allowed = scope.AllowedBranchIds(orgBranchIds);
        if (allowed is null) return Forbid();

        if (request.GroupId is null && scope.Scope != ScopeType.AllOrganization)
            return BadRequest(new { error = "Guruhni tanlang: guruhsiz student filial ro'yxatida ko'rinmaydi." });

        AdmissionGroup? group = null;
        if (request.GroupId is { } groupId)
        {
            var admission = await EnrollmentRules.CheckAsync(db, scope.OrganizationId, groupId, allowed, ct: ct);
            switch (admission.Problem)
            {
                case null: group = admission.Group; break;
                case AdmissionProblem.NotFound: return NotFound(new { error = admission.Message });
                case AdmissionProblem.OutOfScope: return Forbid();
                default: return Conflict(new { error = admission.Message });
            }
        }

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict(new { error = "Bu email bilan foydalanuvchi allaqachon mavjud." });
        if (number is not null && await db.Students.AnyAsync(s => s.StudentNumber == number, ct))
            return Conflict(new { error = "Bu student raqami band." });

        var studentRole = await db.Roles.FirstOrDefaultAsync(r => r.Code == RoleCode.Student, ct);
        if (studentRole is null) return Problem("STUDENT roli topilmadi — seed ishga tushirilmagan bo'lishi mumkin.");

        var user = new User(scope.OrganizationId, fullName, phone, email, passwordHash: string.Empty);
        user.SetPasswordHash(Hasher.HashPassword(user, request.Password));
        user.AssignRole(studentRole.Id);
        if (group is not null) user.AssignBranch(group.BranchId);
        // Whoever registered this student chose the password, not the student — S01.05 forces a change on first use.
        user.RequirePasswordChange();
        db.Users.Add(user);

        var student = new Student(scope.OrganizationId, user.Id, number ?? await StudentNumbers.NextAsync(db, ct), joined, level);
        db.Students.Add(student);

        Enrollment? enrollment = null;
        if (group is not null)
        {
            enrollment = new Enrollment(scope.OrganizationId, student.Id, group.Id);
            db.Enrollments.Add(enrollment);
            db.StudentTimelineEvents.Add(new StudentTimelineEvent(scope.OrganizationId, student.Id, TimelineEventType.Enrolled,
                JsonSerializer.Serialize(new { groupId = group.Id, byUserId = scope.UserId })));
        }

        await db.SaveChangesAsync(ct);

        // Same as a lead's conversion: the first month is invoiced now instead of on the next sweep, and a billing
        // hiccup must not undo a registration that has already been saved — the sweep issues it later.
        if (enrollment is not null)
        {
            try
            {
                await billing.IssueDueInvoicesAsync(DateTimeOffset.UtcNow, enrollmentId: enrollment.Id, ct: ct);
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "First invoice for enrollment {EnrollmentId} not issued at registration; the sweep will retry.", enrollment.Id);
            }
        }

        return CreatedAtAction(nameof(StudentsController.GetById), "Students", new { id = student.Id },
            new StudentDto(student.Id, student.UserId, user.FullName, user.Email, user.Phone, student.StudentNumber,
                student.JoinedDate, student.CurrentLevel, student.Status));
    }

    /// <summary>Anything on the profile: name, phone, level, join date, student number.</summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "students.edit")]
    public Task<ActionResult<StudentDto>> Update(Guid id, UpdateStudentRequest request, CancellationToken ct = default) =>
        ApplyAsync(id, request, ct);

    /// <summary>Only the name and the phone number — what the matrix leaves an operator ("limited (contact info)").
    /// The same change as <see cref="Update"/> with every other field held back, so the two cannot drift apart.</summary>
    [HttpPatch("{id:guid}/contact")]
    [Authorize(Policy = "students.contact.edit")]
    public Task<ActionResult<StudentDto>> UpdateContact(Guid id, UpdateStudentContactRequest request, CancellationToken ct = default) =>
        ApplyAsync(id, new UpdateStudentRequest(request.FullName, request.Phone, null, null, null), ct);

    private async Task<ActionResult<StudentDto>> ApplyAsync(Guid id, UpdateStudentRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (!await scope.CanAccessStudentAsync(db, id)) return Forbid();

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id && s.OrganizationId == scope.OrganizationId, ct);
        if (student is null) return NotFound();
        var user = await db.Users.FirstAsync(u => u.Id == student.UserId, ct);

        var fullName = request.FullName is null ? user.FullName : request.FullName.Trim();
        var phone = request.Phone is null ? user.Phone : request.Phone.Trim();
        var level = request.CurrentLevel is null ? student.CurrentLevel : Clean(request.CurrentLevel);
        var joined = request.JoinedDate ?? student.JoinedDate;
        var number = request.StudentNumber is null ? student.StudentNumber : request.StudentNumber.Trim();

        if (ProfileProblem(fullName, phone, level) is { } profileProblem) return BadRequest(new { error = profileProblem });
        if (number.Length == 0 || number.Length > MaxNumberLength)
            return BadRequest(new { error = $"Student raqami 1–{MaxNumberLength} belgidan iborat bo'lsin." });
        if (request.JoinedDate is not null && JoinedInFuture(joined, DateOnly.FromDateTime(DateTime.UtcNow)))
            return BadRequest(new { error = "Qo'shilgan sana kelajakda bo'lmasin." });

        if (number != student.StudentNumber && await db.Students.AnyAsync(s => s.StudentNumber == number && s.Id != id, ct))
            return Conflict(new { error = "Bu student raqami band." });

        user.UpdateProfile(fullName, phone);
        student.UpdateProfile(joined, level);
        if (number != student.StudentNumber) student.Renumber(number);

        await db.SaveChangesAsync(ct);

        return Ok(new StudentDto(student.Id, student.UserId, user.FullName, user.Email, user.Phone, student.StudentNumber,
            student.JoinedDate, student.CurrentLevel, student.Status));
    }

    /// <summary>Trimmed, with blank meaning "none".</summary>
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ProfileProblem(string fullName, string phone, string? level)
    {
        if (fullName.Length == 0) return "Ism-familiyani kiriting.";
        if (fullName.Length > MaxNameLength) return $"Ism-familiya {MaxNameLength} belgidan oshmasin.";
        if (phone.Length == 0) return "Telefon raqamini kiriting.";
        if (phone.Length > MaxPhoneLength) return $"Telefon {MaxPhoneLength} belgidan oshmasin.";
        if (level is { Length: > MaxLevelLength }) return $"Daraja {MaxLevelLength} belgidan oshmasin.";
        return null;
    }

    /// <summary>A day of slack: at 02:00 in Tashkent the local date is already a day ahead of UTC's, and a person
    /// entering "today" must not be told it lies in the future.</summary>
    private static bool JoinedInFuture(DateOnly joined, DateOnly today) => joined > today.AddDays(1);
}
