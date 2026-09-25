using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Authorization;

/// <summary>
/// Resolves whether a user holds a permission code and with which ScopeType. Three layers, most specific first: the per-user override
/// (<c>user_permissions</c>), then the organization's change to the role's cell of the matrix (<c>role_permission_overrides</c>, S07.02 —
/// which may also take the permission away), then the grant the role ships with (<c>role_permissions</c>, docs/01_role_permission_matrix.md §1).
/// Used by the policy handler, and directly where one request needs several codes or there is no policy pipeline (the chat hub).
/// </summary>
public class PermissionResolver(AppDbContext db)
{
    public async Task<ResolvedScope?> ResolveAsync(Guid userId, string permissionCode, CancellationToken ct = default)
    {
        var user = await db.Users
            .Include(u => u.Roles)
            .Include(u => u.Branches)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var overrideScope = await db.UserPermissions
            .Where(up => up.UserId == userId)
            .Join(db.Permissions, up => up.PermissionId, p => p.Id, (up, p) => new { up.ScopeOverride, p.Code })
            .Where(x => x.Code == permissionCode)
            .Select(x => (ScopeType?)x.ScopeOverride)
            .FirstOrDefaultAsync(ct);

        var scope = overrideScope;
        if (scope is null)
        {
            var roleIds = user.Roles.Select(r => r.RoleId).ToList();
            var defaults = await db.RolePermissions
                .Where(rp => roleIds.Contains(rp.RoleId))
                .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { rp.RoleId, rp.DefaultScope, p.Code })
                .Where(x => x.Code == permissionCode)
                .ToListAsync(ct);
            var layers = await db.RolePermissionOverrides
                .Where(o => o.OrganizationId == user.OrganizationId && o.PermissionCode == permissionCode && roleIds.Contains(o.RoleId))
                .ToListAsync(ct);

            // The first role that ends up holding the permission decides, as before — a revoked role passes to the next.
            scope = roleIds
                .Select(roleId => RolePermissionOverride.Effective(
                    defaults.Where(d => d.RoleId == roleId).Select(d => (ScopeType?)d.DefaultScope).FirstOrDefault(),
                    layers.FirstOrDefault(o => o.RoleId == roleId)))
                .FirstOrDefault(s => s is not null);
        }
        if (scope is null)
        {
            // Not held by role or override: an approved, unexpired production-data request (TZ §3.8) is the only other way in.
            var now = DateTimeOffset.UtcNow;
            var granted = await db.DataAccessRequests.AnyAsync(r =>
                r.RequesterId == userId && r.PermissionCode == permissionCode
                && r.Status == DataAccessStatus.Approved && r.ExpiresAt > now, ct);
            if (!granted) return null;
            scope = ScopeType.AllOrganization;
        }

        return new ResolvedScope
        {
            Scope = scope.Value,
            UserId = userId,
            OrganizationId = user.OrganizationId,
            BranchIds = user.Branches.Select(b => b.BranchId).ToList()
        };
    }

    /// <summary>The roles that hold <paramref name="permissionCode"/> at all in the organization — the default grant minus what the matrix
    /// took away, plus what it added. For "who could hold this?" pre-filters that must agree with <see cref="ResolveAsync"/>; the scope
    /// each holder ends up with still comes from resolving them.</summary>
    public static IQueryable<Guid> RolesHolding(AppDbContext db, Guid organizationId, string permissionCode)
    {
        var layers = db.RolePermissionOverrides.Where(o => o.OrganizationId == organizationId && o.PermissionCode == permissionCode);
        var taken = layers.Where(o => o.Revoked).Select(o => o.RoleId);
        var added = layers.Where(o => !o.Revoked && o.ScopeOverride != null).Select(o => o.RoleId);
        var shipped = db.RolePermissions
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { rp.RoleId, p.Code })
            .Where(x => x.Code == permissionCode)
            .Select(x => x.RoleId);
        return shipped.Where(roleId => !taken.Contains(roleId)).Union(added);
    }

    /// <summary>Every permission the user holds with its effective scope — the same layering as <see cref="ResolveAsync"/>
    /// (user override, then the role's matrix override, then the role default), in one pass. Backs the UI, which hides what the
    /// caller cannot do instead of showing a button that ends in "Bu amal uchun ruxsat yo'q".</summary>
    public async Task<Dictionary<string, ScopeType>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var roleIds = await db.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync(ct);
        var organizationId = await db.Users.Where(u => u.Id == userId).Select(u => u.OrganizationId).FirstOrDefaultAsync(ct);

        var defaults = await db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => new { rp.RoleId, p.Code, rp.DefaultScope })
            .ToListAsync(ct);
        var layers = await db.RolePermissionOverrides
            .Where(o => o.OrganizationId == organizationId && roleIds.Contains(o.RoleId))
            .ToListAsync(ct);
        var overrides = await db.UserPermissions
            .Where(up => up.UserId == userId)
            .Join(db.Permissions, up => up.PermissionId, p => p.Id, (up, p) => new { p.Code, up.ScopeOverride })
            .ToListAsync(ct);

        var result = new Dictionary<string, ScopeType>();
        // A permission only a matrix override grants has no default row, so the codes come from both.
        var codes = defaults.Select(d => d.Code).Concat(layers.Select(o => o.PermissionCode)).Distinct();
        foreach (var code in codes)
        {
            foreach (var roleId in roleIds)
            {
                var codeDefault = defaults.Where(d => d.RoleId == roleId && d.Code == code).Select(d => (ScopeType?)d.DefaultScope).FirstOrDefault();
                var effective = RolePermissionOverride.Effective(codeDefault, layers.FirstOrDefault(o => o.RoleId == roleId && o.PermissionCode == code));
                if (effective is { } scope && result.TryAdd(code, scope)) break;
            }
        }
        foreach (var row in overrides) result[row.Code] = row.ScopeOverride;

        var now = DateTimeOffset.UtcNow;
        var temporary = await db.DataAccessRequests
            .Where(r => r.RequesterId == userId && r.Status == DataAccessStatus.Approved && r.ExpiresAt > now)
            .Select(r => r.PermissionCode).ToListAsync(ct);
        foreach (var code in temporary) result.TryAdd(code, ScopeType.AllOrganization);
        return result;
    }
}
