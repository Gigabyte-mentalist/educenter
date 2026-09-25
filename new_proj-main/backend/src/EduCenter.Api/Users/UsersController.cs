using EduCenter.Api.Auth;
using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Users;

public record UserDto(Guid Id, string FullName, string Email, string Phone, UserStatus Status, List<string> Roles, List<Guid> BranchIds);
public record CreateUserRequest(string FullName, string Email, string Phone, string Password, string RoleCode, List<Guid>? BranchIds);
public record UpdateUserRequest(string FullName, string Phone);
public record UserBranchDto(Guid Id, string Name);
public record UserGroupDto(Guid Id, string Name, string Role);
public record UserProfileDto(
    Guid Id, string FullName, string Email, string Phone, UserStatus Status,
    List<string> Roles, List<UserBranchDto> Branches, List<UserGroupDto> Groups,
    Guid? StudentId, Guid? ParentId, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
public record AssignRoleRequest(string RoleCode);
public record ResetPasswordRequest(string? NewPassword);
public record ResetPasswordResponse(string TemporaryPassword);

[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext db, AccountBlocker accountBlocker, UserPhotoStorage photos, TeacherCredentialFileStorage credentialFiles) : ControllerBase
{
    private static readonly PasswordHasher<User> Hasher = new();

    /// <summary>Roles a branch-scoped Administrator may hand out (docs/01_role_permission_matrix.md §3.2:
    /// "users.create — AD: MY_BRANCHES (operator/teacher)"). Granting SUPER_ADMIN or ADMINISTRATOR from a
    /// branch scope would be privilege escalation, so only an org-wide scope can do that.</summary>
    private static readonly HashSet<string> BranchScopedAssignableRoles =
    [
        RoleCode.Operator, RoleCode.Teacher, RoleCode.SupportTeacher, RoleCode.Student, RoleCode.Parent,
    ];

    /// <summary>Roles that make someone a member of the school's team rather than a customer of it. The split
    /// exists because one flat list of "users" put every student and parent on the staff screen, where an
    /// administrator is looking for a colleague and has to scroll past a thousand learners to find one.</summary>
    public static readonly string[] StaffRoles =
    [
        RoleCode.SuperAdmin, RoleCode.Administrator, RoleCode.Operator,
        RoleCode.Teacher, RoleCode.SupportTeacher, RoleCode.Developer,
    ];

    public static readonly string[] LearnerRoles = [RoleCode.Student, RoleCode.Parent];

    [HttpGet]
    [Authorize(Policy = "users.view")]
    public async Task<ActionResult<Page<UserDto>>> List(
        [FromQuery] string? role, [FromQuery] Guid? branchId, [FromQuery] string? search,
        [FromQuery] string? audience, [FromQuery] UserStatus? status, [FromQuery] PageRequest? paging,
        CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var page = paging ?? new PageRequest();

        IQueryable<User> query = db.Users
            .Include(u => u.Roles)
            .Include(u => u.Branches)
            .Where(u => u.OrganizationId == scope.OrganizationId);

        query = scope.Scope switch
        {
            ScopeType.AllOrganization => query,
            ScopeType.MyBranches => query.Where(u => u.Branches.Any(b => scope.BranchIds.Contains(b.BranchId))),
            ScopeType.Self => query.Where(u => u.Id == scope.UserId),
            _ => query.Where(_ => false) // no flat listing for MyGroups/AssignedGroups/MyChildren
        };

        if (branchId is not null)
            query = query.Where(u => u.Branches.Any(b => b.BranchId == branchId));

        if (status is not null)
            query = query.Where(u => u.Status == status);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(u =>
                EF.Functions.ILike(u.FullName, term) ||
                EF.Functions.ILike(u.Email, term) ||
                EF.Functions.ILike(u.Phone, term));
        }

        // Role filtering runs in SQL, not over the materialised page: filtering afterwards would return
        // fewer rows than the page size and a Total that counts users the filter excludes.
        var wantedRoles = role is not null
            ? [role]
            : audience?.ToLowerInvariant() switch
            {
                "staff" => StaffRoles,
                "learners" => LearnerRoles,
                _ => []
            };

        if (wantedRoles.Length > 0)
        {
            var roleIds = await db.Roles.Where(r => wantedRoles.Contains(r.Code)).Select(r => r.Id).ToListAsync(ct);
            query = query.Where(u => u.Roles.Any(ur => roleIds.Contains(ur.RoleId)));
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip(page.Skip).Take(page.Take)
            .ToListAsync(ct);

        var pageRoleIds = users.SelectMany(u => u.Roles.Select(r => r.RoleId)).Distinct().ToList();
        var roleCodesById = await db.Roles.Where(r => pageRoleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Code, ct);

        var items = users
            .Select(u => new UserDto(
                u.Id, u.FullName, u.Email, u.Phone, u.Status,
                u.Roles.Select(r => roleCodesById.GetValueOrDefault(r.RoleId, "?")).ToList(),
                u.Branches.Select(b => b.BranchId).ToList()))
            .ToList();

        return Ok(new Page<UserDto>(items, total, page.Number, page.Take));
    }

    /// <summary>
    /// One person's profile. The list view answers "who is in this branch"; this answers "who is this", so it
    /// resolves the things a bare UserDto only carries ids for — branch names, the groups they teach, and the
    /// student or parent card the account is attached to, which is how the profile links onward.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "users.view")]
    public async Task<ActionResult<UserProfileDto>> Get(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();

        var user = await db.Users.Include(u => u.Roles).Include(u => u.Branches)
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == scope.OrganizationId, ct);
        if (user is null) return NotFound();

        // Anyone may open their own profile; reaching someone else's needs the scope that would have listed them.
        if (user.Id != scope.UserId && !await CanTouchUserAsync(scope, user, ct)) return Forbid();

        var roleIds = user.Roles.Select(r => r.RoleId).ToList();
        var roles = await db.Roles.Where(r => roleIds.Contains(r.Id)).Select(r => r.Code).ToListAsync(ct);

        var branchIds = user.Branches.Select(b => b.BranchId).ToList();
        var branches = await db.Branches.Where(b => branchIds.Contains(b.Id))
            .Select(b => new UserBranchDto(b.Id, b.Name)).ToListAsync(ct);

        var groups = await (
            from s in db.GroupStaff
            join g in db.Groups on s.GroupId equals g.Id
            where s.UserId == id
            select new UserGroupDto(g.Id, g.Name, s.Role.ToString())
        ).ToListAsync(ct);

        var studentId = await db.Students.Where(s => s.UserId == id).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        var parentId = await db.Parents.Where(p => p.UserId == id).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);

        return Ok(new UserProfileDto(
            user.Id, user.FullName, user.Email, user.Phone, user.Status, roles, branches, groups,
            studentId, parentId, user.CreatedAt, user.UpdatedAt));
    }

    [HttpPost]
    [Authorize(Policy = "users.create")]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        if (PasswordPolicy.Validate(request.Password) is { } passwordProblem)
            return BadRequest(new { error = passwordProblem });

        if (request.RoleCode == RoleCode.Student)
            return BadRequest(new { error = "Student /students sahifasidan yaratiladi — u yerda guruh va profil ham yaratiladi." });

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == request.RoleCode);
        if (role is null) return BadRequest(new { error = $"Noma'lum rol: '{request.RoleCode}'." });

        var roleCheck = EnsureRoleAssignable(scope, role.Code);
        if (roleCheck is not null) return roleCheck;

        var branchIds = request.BranchIds ?? [];
        var branchCheck = await EnsureBranchesInScopeAsync(scope, branchIds);
        if (branchCheck is not null) return branchCheck;

        if (await db.Users.AnyAsync(u => u.Email == request.Email.Trim().ToLowerInvariant()))
            return Conflict(new { error = "Bu email bilan foydalanuvchi allaqachon mavjud." });

        var user = new User(scope.OrganizationId, request.FullName, request.Phone, request.Email, passwordHash: string.Empty);
        user.SetPasswordHash(Hasher.HashPassword(user, request.Password));
        user.AssignRole(role.Id);
        // Whoever created this account chose the password, not its holder — S01.05 forces a change on first use.
        user.RequirePasswordChange();

        foreach (var branchId in branchIds)
            user.AssignBranch(branchId);

        db.Users.Add(user);

        // A PARENT account is useless without the row that everything else (StudentAccess, dashboard, TZ §24)
        // joins against — leaving it unwritten was the very hole this endpoint is closing.
        if (role.Code == RoleCode.Parent)
            db.Parents.Add(new Parent(scope.OrganizationId, user.Id));

        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new UserDto(
            user.Id, user.FullName, user.Email, user.Phone, user.Status,
            [role.Code], branchIds));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "users.edit")]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();

        var user = await db.Users.Include(u => u.Roles).Include(u => u.Branches)
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == scope.OrganizationId, ct);
        if (user is null) return NotFound();

        if (!await CanTouchUserAsync(scope, user, ct)) return Forbid();

        // FullName/Phone are the only self-editable fields in Phase 1 — email/role changes go through
        // dedicated endpoints so they stay auditable.
        user.UpdateProfile(request.FullName, request.Phone);
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/roles")]
    [Authorize(Policy = "role.assign")]
    public async Task<IActionResult> AssignRole(Guid id, AssignRoleRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();

        var user = await db.Users.Include(u => u.Roles).Include(u => u.Branches)
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == scope.OrganizationId, ct);
        if (user is null) return NotFound();

        if (!await CanTouchUserAsync(scope, user, ct)) return Forbid();

        if (request.RoleCode == RoleCode.Student)
            return BadRequest(new { error = "Student /students sahifasidan yaratiladi — u yerda guruh va profil ham yaratiladi." });

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == request.RoleCode);
        if (role is null) return BadRequest(new { error = $"Noma'lum rol: '{request.RoleCode}'." });

        var roleCheck = EnsureRoleAssignable(scope, role.Code);
        if (roleCheck is not null) return roleCheck;

        if (user.Roles.Any(ur => ur.RoleId == role.Id))
            return NoContent(); // already has it

        user.AssignRole(role.Id);

        if (role.Code == RoleCode.Parent && !await db.Parents.AnyAsync(p => p.UserId == user.Id))
            db.Parents.Add(new Parent(user.OrganizationId, user.Id));

        await db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Blocks the account without touching anything it is attached to. This is the everyday
    /// "this person left" action — their attendance marks, payments and messages stay readable.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "users.deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var user = await db.Users.Include(u => u.Roles).Include(u => u.Branches)
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == scope.OrganizationId, ct);
        if (user is null) return NotFound();
        if (!await CanTouchUserAsync(scope, user, ct)) return Forbid();
        if (user.Id == scope.UserId)
            return BadRequest(new { error = "O'z hisobingizni bloklay olmaysiz." });

        user.Deactivate();
        // Revoked, not deleted: AuthController.Refresh needs the row to still exist so it can answer
        // ACCOUNT_BLOCKED instead of the generic "token invalid" a deleted row would produce (S01.04).
        await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);
        await db.SaveChangesAsync(ct);
        await accountBlocker.StatusChangedAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = "users.deactivate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var user = await db.Users.Include(u => u.Roles).Include(u => u.Branches)
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == scope.OrganizationId, ct);
        if (user is null) return NotFound();
        if (!await CanTouchUserAsync(scope, user, ct)) return Forbid();

        user.Activate();
        await db.SaveChangesAsync(ct);
        await accountBlocker.StatusChangedAsync(id, ct);
        return NoContent();
    }

    /// <summary>An administrator's reset, not the holder's own choice — unlike <c>POST /api/auth/change-password</c>,
    /// this sets <see cref="User.MustChangePassword"/> so the temporary password returned here is good for nothing
    /// but replacing itself, and revokes every session so a device left signed in does not skip that requirement.</summary>
    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = "users.password.reset")]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var user = await db.Users.Include(u => u.Roles).Include(u => u.Branches)
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == scope.OrganizationId, ct);
        if (user is null) return NotFound();
        if (!await CanTouchUserAsync(scope, user, ct)) return Forbid();
        if (user.Id == scope.UserId)
            return BadRequest(new { error = "O'z parolingiz uchun \"Parolni o'zgartirish\"dan foydalaning." });

        var newPassword = string.IsNullOrWhiteSpace(request.NewPassword) ? PasswordGenerator.Generate() : request.NewPassword;
        if (PasswordPolicy.Validate(newPassword) is { } passwordProblem) return BadRequest(new { error = passwordProblem });

        user.SetPasswordHash(Hasher.HashPassword(user, newPassword));
        user.RequirePasswordChange();

        // Every existing session must hit the forced-change screen next, not skip it on an already-signed-in device.
        await db.RefreshTokens.Where(t => t.UserId == id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow), ct);

        await db.SaveChangesAsync(ct);
        await accountBlocker.StatusChangedAsync(id, ct);

        return Ok(new ResetPasswordResponse(newPassword));
    }

    /// <summary>
    /// Erases the account. Nothing but user_roles and user_branches has a database-level foreign key to a
    /// user, so a delete that ignored the rest would leave a student profile, a group, an invoice or a chat
    /// message pointing at an id that no longer resolves to a name — the row would survive looking corrupted
    /// rather than deleted. So everything that references the user is counted first and the delete is refused
    /// with the list; deactivation is what the caller wants in that case, and the response says so.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "users.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var user = await db.Users.Include(u => u.Branches).Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == scope.OrganizationId, ct);
        if (user is null) return NotFound();
        if (!await CanTouchUserAsync(scope, user, ct)) return Forbid();
        if (user.Id == scope.UserId)
            return BadRequest(new { error = "O'z hisobingizni o'chira olmaysiz." });

        var blockers = new List<string>();
        if (await db.Students.AnyAsync(s => s.UserId == id, ct)) blockers.Add("o'quvchi kartasi");
        if (await db.Parents.AnyAsync(p => p.UserId == id, ct)) blockers.Add("ota-ona kartasi");
        if (await db.GroupStaff.AnyAsync(s => s.UserId == id, ct)) blockers.Add("guruhlarga biriktirilgan");
        if (await db.Lessons.AnyAsync(l => l.TeacherId == id, ct)) blockers.Add("darslar jadvali");
        if (await db.ChatMessages.AnyAsync(m => m.SenderId == id, ct)) blockers.Add("chat xabarlari");
        if (await db.Payments.AnyAsync(p => p.ReceivedBy == id, ct)) blockers.Add("qabul qilingan to'lovlar");

        if (blockers.Count > 0)
            return Conflict(new
            {
                error = $"Bu foydalanuvchini o'chirib bo'lmaydi — unga bog'liq ma'lumotlar bor: {string.Join(", ", blockers)}. "
                    + "Buning o'rniga hisobni bloklang (deaktivatsiya) — tarix saqlanadi, lekin u tizimga kira olmaydi.",
                blockers,
                canDeactivate = true,
            });

        // Session and delivery state carry no history worth keeping, so they go with the account rather than
        // turning into another blocker.
        await db.RefreshTokens.Where(t => t.UserId == id).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(n => n.UserId == id).ExecuteDeleteAsync(ct);
        await db.NotificationPreferences.Where(p => p.UserId == id).ExecuteDeleteAsync(ct);
        await db.UserPermissions.Where(p => p.UserId == id).ExecuteDeleteAsync(ct);
        await db.ChatMembers.Where(m => m.UserId == id).ExecuteDeleteAsync(ct);

        var photoKey = user.PhotoKey;
        var credentialFileKeys = await db.TeacherCredentials.Where(c => c.UserId == id && c.StorageKey != null)
            .Select(c => c.StorageKey!).ToListAsync(ct);
        db.Users.Remove(user); // user_roles / user_branches and the teacher portfolio rows cascade
        await db.SaveChangesAsync(ct);
        if (photoKey is not null) photos.Delete(photoKey); // the picture belongs to the account: it must not outlive it on disk
        foreach (var key in credentialFileKeys) credentialFiles.Delete(key); // so do the scans of a teacher's certificates
        await accountBlocker.StatusChangedAsync(id, ct);
        return NoContent();
    }

    /// <summary>See <see cref="UserAccess.CanTouchAsync"/> — the rule is shared with the profile-picture endpoints.</summary>
    private Task<bool> CanTouchUserAsync(ResolvedScope scope, User user, CancellationToken ct) => scope.CanTouchAsync(db, user, ct);

    private ActionResult? EnsureRoleAssignable(ResolvedScope scope, string roleCode)
    {
        if (scope.Scope == ScopeType.AllOrganization) return null;
        if (BranchScopedAssignableRoles.Contains(roleCode)) return null;

        return StatusCode(StatusCodes.Status403Forbidden,
            new { error = $"'{roleCode}' rolini faqat tashkilot darajasidagi administrator bera oladi." });
    }

    private async Task<ActionResult?> EnsureBranchesInScopeAsync(ResolvedScope scope, List<Guid> branchIds)
    {
        if (branchIds.Count == 0) return null;

        var orgBranchIds = await db.Branches
            .Where(b => b.OrganizationId == scope.OrganizationId)
            .Select(b => b.Id)
            .ToListAsync();

        if (branchIds.Any(id => !orgBranchIds.Contains(id)))
            return BadRequest(new { error = "Ko'rsatilgan filial tashkilotga tegishli emas." });

        if (scope.Scope == ScopeType.AllOrganization) return null;

        if (branchIds.Any(id => !scope.BranchIds.Contains(id)))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Faqat o'zingizga biriktirilgan filiallarga foydalanuvchi qo'sha olasiz." });

        return null;
    }
}
