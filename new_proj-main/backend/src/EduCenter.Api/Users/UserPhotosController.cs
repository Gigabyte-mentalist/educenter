using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Users;

/// <summary>Multipart form of the upload: one image under the field <c>file</c>.</summary>
public class UserPhotoForm
{
    public IFormFile? File { get; set; }
}

/// <param name="Version">Changes with every new picture; the address of the picture carries it (<c>?v=</c>) so a browser that has
/// cached the old one asks again.</param>
public record UserPhotoDto(string Version);

/// <summary>
/// A profile picture for any account (docs/21.09-audit-jurnali S04.04, TZ §7 header): a student's on their profile, a teacher's on
/// theirs. Changing it takes <c>users.edit</c> and the same reach as editing the account — a person may change their own, a branch
/// Administrator those of their branch's people, but not a Super Admin's (<see cref="UserAccess.CanTouchAsync"/>). Seeing it takes
/// the reach that lets the caller open that person: their profile, one of their children's, or a group they teach.
/// </summary>
[ApiController]
[Route("api/users/{id:guid}/photo")]
public class UserPhotosController(AppDbContext db, UserPhotoStorage storage) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "users.edit")]
    [RequestSizeLimit(4 * 1024 * 1024)] // above the 2 MB rule, so an oversized picture gets the readable 400 rather than a bare 413
    public async Task<ActionResult<UserPhotoDto>> Upload(Guid id, [FromForm] UserPhotoForm form, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var user = await LoadAsync(scope, id, ct);
        if (user is null) return NotFound();
        if (!await scope.CanTouchAsync(db, user, ct)) return Forbid();

        if (form.File is null) return BadRequest(new { error = "Foto tanlanmagan." });
        if (await storage.ValidateAsync(form.File, ct) is { } problem) return BadRequest(new { error = problem });

        var key = await storage.SaveAsync(form.File, ct);
        var previous = user.PhotoKey;
        user.SetPhoto(key);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            storage.Delete(key);
            throw;
        }

        if (previous is not null) storage.Delete(previous);
        return Ok(new UserPhotoDto(UserPhotoStorage.VersionOf(key)));
    }

    [HttpDelete]
    [Authorize(Policy = "users.edit")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var user = await LoadAsync(scope, id, ct);
        if (user is null) return NotFound();
        if (!await scope.CanTouchAsync(db, user, ct)) return Forbid();

        if (user.PhotoKey is { } key)
        {
            user.SetPhoto(null);
            await db.SaveChangesAsync(ct);
            storage.Delete(key);
        }
        return NoContent();
    }

    /// <summary>The picture itself, fetched with the caller's token (an <c>&lt;img src&gt;</c> cannot send one). Served as an image
    /// and nothing else: the type comes from the stored extension, and the browser is told not to guess.</summary>
    [HttpGet]
    [Authorize(Policy = "users.view")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var user = await LoadAsync(scope, id, ct);
        if (user?.PhotoKey is null) return NotFound();
        if (!await CanSeeAsync(scope, user, ct)) return Forbid();

        var path = storage.PathFor(user.PhotoKey);
        if (path is null) return NotFound();

        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, UserPhotoStorage.ContentTypeFor(user.PhotoKey));
    }

    private Task<User?> LoadAsync(ResolvedScope scope, Guid id, CancellationToken ct) =>
        db.Users.Include(u => u.Roles).Include(u => u.Branches)
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == scope.OrganizationId, ct);

    private async Task<bool> CanSeeAsync(ResolvedScope scope, User user, CancellationToken ct)
    {
        if (user.Id == scope.UserId || scope.Scope == ScopeType.AllOrganization) return true;

        // A student: whoever may open the student's profile.
        var studentId = await db.Students.Where(s => s.UserId == user.Id).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (studentId is { } student) return await scope.CanAccessStudentAsync(db, student);

        // A guardian: whoever may open the profile of one of their children.
        var scoped = scope.ScopedStudentIds(db);
        if (scoped is not null)
        {
            var children = db.StudentParents.Where(sp => db.Parents.Any(p => p.Id == sp.ParentId && p.UserId == user.Id)).Select(sp => sp.StudentId);
            if (await scoped.AnyAsync(s => children.Contains(s), ct)) return true;
        }

        // Anyone else (staff): a colleague in the caller's branch, or someone who teaches a group the caller may see — so a student
        // sees their teacher, a parent their child's, a teacher the co-teacher.
        if (scope.Scope == ScopeType.MyBranches && user.Branches.Any(b => scope.BranchIds.Contains(b.BranchId))) return true;
        var groups = await scope.ScopedGroupsAsync(db);
        return await db.GroupStaff.AnyAsync(gs => gs.UserId == user.Id && groups.Any(g => g.Id == gs.GroupId), ct);
    }
}
