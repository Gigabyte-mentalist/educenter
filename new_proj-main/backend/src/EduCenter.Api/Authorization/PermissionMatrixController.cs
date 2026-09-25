using EduCenter.Infrastructure;
using EduCenter.Infrastructure.Seed;
using EduCenter.Modules.Identity.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Authorization;

public record MatrixRoleDto(string Code, string Name);

/// <param name="DefaultScope">What the role ships with (null: it does not hold the permission by default).</param>
/// <param name="OverrideId">Set when the Super Admin changed this cell; the cell is then "changed" and can be put back.</param>
/// <param name="EffectiveScope">What the role actually gets now — the override over the default; null: nothing.</param>
public record MatrixCellDto(string RoleCode, ScopeType? DefaultScope, Guid? OverrideId, ScopeType? OverrideScope, bool Revoked, ScopeType? EffectiveScope);

/// <param name="AllowedScopes">The scopes that make sense for this permission — the ones its shipped grants use. A cell may only be set to one of them.</param>
public record MatrixPermissionDto(string Code, string Resource, string Action, ScopeType[] AllowedScopes, List<MatrixCellDto> Cells);

public record PermissionMatrixDto(List<MatrixRoleDto> Roles, List<MatrixPermissionDto> Permissions);

/// <param name="Scope">The new scope for the role; null with <paramref name="Revoked"/> false puts the cell back to its default.</param>
/// <param name="Revoked">Takes the permission away from the role. Not combinable with a scope.</param>
public record SetOverrideRequest(string RoleCode, string PermissionCode, ScopeType? Scope, bool Revoked);

/// <summary>
/// The role × permission matrix, editable by the Super Admin (docs/21.09-audit-jurnali S07.02, TZ §4). The grants in code stay the
/// defaults; an edit is stored as an override on top of them, which the resolver applies on every request — so it takes effect at
/// once and survives the seeder rewriting the defaults on the next start.
/// </summary>
[ApiController]
[Route("api/permissions")]
[Authorize(Policy = "permission.policy.edit")]
public class PermissionMatrixController(AppDbContext db) : ControllerBase
{
    /// <summary>Cells the Super Admin's own role must keep exactly as shipped — without them nobody could open this screen again, sign an
    /// account out or hand a role out, and the product would be locked from the inside.</summary>
    private static readonly string[] SuperAdminLockout = ["permission.policy.edit", "users.view", "users.deactivate", "role.assign"];

    /// <summary>What a Developer must never be given, whatever the matrix says: money, accounts and the permission system itself. The same
    /// line <see cref="DataAccessRequest.GrantableCodes"/> draws for temporary access — a fault in a payment is found in the logs, not by
    /// browsing everyone's money.</summary>
    private static readonly string[] DeveloperForbiddenResources = ["payments", "finance", "discount", "promo", "refund", "users", "role", "permission"];

    [HttpGet("matrix")]
    public async Task<ActionResult<PermissionMatrixDto>> Matrix(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization) return Forbid();

        var roles = await db.Roles.ToListAsync(ct);
        var permissions = await db.Permissions.OrderBy(p => p.Resource).ThenBy(p => p.Code).ToListAsync(ct);
        var defaults = await db.RolePermissions.ToListAsync(ct);
        var layers = await db.RolePermissionOverrides.Where(o => o.OrganizationId == scope.OrganizationId).ToListAsync(ct);

        // The roles in the order the matrix document lists them, not the order the database happens to return.
        var ordered = PermissionSeedData.Roles
            .Select(r => roles.FirstOrDefault(role => role.Code == r.Code))
            .Where(role => role is not null).Select(role => role!)
            .Concat(roles.Where(role => PermissionSeedData.Roles.All(r => r.Code != role.Code)))
            .ToList();

        var result = new List<MatrixPermissionDto>();
        foreach (var permission in permissions)
        {
            var cells = ordered.Select(role => Cell(role,
                defaults.FirstOrDefault(d => d.RoleId == role.Id && d.PermissionId == permission.Id)?.DefaultScope,
                layers.FirstOrDefault(o => o.RoleId == role.Id && o.PermissionId == permission.Id))).ToList();
            var allowed = AllowedScopes(permission.Code, defaults.Where(d => d.PermissionId == permission.Id).Select(d => d.DefaultScope));
            result.Add(new MatrixPermissionDto(permission.Code, permission.Resource, permission.Action, allowed, cells));
        }

        return Ok(new PermissionMatrixDto(ordered.Select(r => new MatrixRoleDto(r.Code, r.Name)).ToList(), result));
    }

    [HttpPut("overrides")]
    public async Task<ActionResult<MatrixCellDto>> Set(SetOverrideRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization) return Forbid();

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Code == request.RoleCode, ct);
        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == request.PermissionCode, ct);
        if (role is null || permission is null) return NotFound(new { error = "Rol yoki ruxsat topilmadi." });

        var codeDefault = await db.RolePermissions
            .Where(rp => rp.RoleId == role.Id && rp.PermissionId == permission.Id)
            .Select(rp => (ScopeType?)rp.DefaultScope).FirstOrDefaultAsync(ct);
        var existing = await db.RolePermissionOverrides.FirstOrDefaultAsync(
            o => o.OrganizationId == scope.OrganizationId && o.RoleId == role.Id && o.PermissionId == permission.Id, ct);

        if (request.Revoked && request.Scope is not null) return BadRequest(new { error = "Ruxsatni olib tashlash bilan doirani birga berib bo'lmaydi." });
        if (request.Scope == ScopeType.None)
            return BadRequest(new { error = "\"Doira yo'q\" bilan ruxsatni olib tashlab bo'lmaydi — ruxsatni olib tashlash tugmasidan foydalaning." });

        if (request.Revoked && codeDefault is null)
            return BadRequest(new { error = "Bu rolda bu ruxsat yo'q — olib tashlanadigan narsa yo'q." });

        if (request.Scope is { } wanted)
        {
            var defaultsOfCode = await db.RolePermissions.Where(rp => rp.PermissionId == permission.Id).Select(rp => rp.DefaultScope).ToListAsync(ct);
            var allowed = AllowedScopes(permission.Code, defaultsOfCode);
            if (!allowed.Contains(wanted))
                return BadRequest(new { error = $"\"{permission.Code}\" uchun bu doira mantiqsiz. Mumkin bo'lganlari: {string.Join(", ", allowed)}." });
        }

        if (Guard(role.Code, permission, request) is { } refusal) return refusal;

        // Asking for what the role already ships with is the same as asking to undo the change.
        var restore = !request.Revoked && (request.Scope is null || request.Scope == codeDefault);
        if (restore)
        {
            if (existing is not null)
            {
                db.RolePermissionOverrides.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
            return Ok(Cell(role, codeDefault, null));
        }

        if (existing is null)
        {
            existing = new RolePermissionOverride(scope.OrganizationId, role.Id, role.Code, permission.Id, permission.Code, request.Scope, request.Revoked, scope.UserId);
            db.RolePermissionOverrides.Add(existing);
        }
        else
        {
            existing.Set(request.Scope, request.Revoked, scope.UserId);
        }

        await db.SaveChangesAsync(ct);
        return Ok(Cell(role, codeDefault, existing));
    }

    [HttpDelete("overrides/{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization) return Forbid();

        var layer = await db.RolePermissionOverrides.FirstOrDefaultAsync(o => o.Id == id && o.OrganizationId == scope.OrganizationId, ct);
        if (layer is null) return NotFound();

        db.RolePermissionOverrides.Remove(layer);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>The rules no edit may break; null when the edit is fine.</summary>
    private ActionResult? Guard(string roleCode, Permission permission, SetOverrideRequest request)
    {
        if (roleCode == RoleCode.SuperAdmin && SuperAdminLockout.Contains(permission.Code))
            return Conflict(new { error = $"Super Admin'ning \"{permission.Code}\" ruxsatini o'zgartirib bo'lmaydi: tizimni o'zingizdan qulflab qo'yasiz." });

        if (permission.Code == "permission.policy.edit" && !request.Revoked && request.Scope is not null && roleCode != RoleCode.SuperAdmin)
            return BadRequest(new { error = "Ruxsatlarni boshqarish huquqi boshqa rolga berilmaydi — u faqat Super Admin'da qoladi." });

        if (roleCode == RoleCode.Developer && !request.Revoked && request.Scope is not null && DeveloperForbiddenResources.Contains(permission.Resource))
            return BadRequest(new { error = "Dasturchiga moliya, hisoblar va ruxsatlar tizimiga kirish berilmaydi (ma'lumotga kirish tartibi bilan bir xil chegara)." });

        return null;
    }

    private static MatrixCellDto Cell(Role role, ScopeType? codeDefault, RolePermissionOverride? layer) =>
        new(role.Code, codeDefault, layer?.Id, layer?.ScopeOverride, layer?.Revoked ?? false, RolePermissionOverride.Effective(codeDefault, layer));

    /// <summary>The scopes that make sense for a permission: those its shipped grants use, in the enum's order (widest first). Never
    /// <see cref="ScopeType.None"/> — "no permission" is a revoked cell, not a scope.</summary>
    private static ScopeType[] AllowedScopes(string code, IEnumerable<ScopeType> storedDefaults) =>
        PermissionSeedData.Grants.Where(g => g.Code == code).Select(g => g.Scope)
            .Concat(storedDefaults)
            .Where(s => s != ScopeType.None)
            .Distinct().Order().ToArray();
}
