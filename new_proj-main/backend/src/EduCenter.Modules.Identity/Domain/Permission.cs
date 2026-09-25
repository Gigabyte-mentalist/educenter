using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;

namespace EduCenter.Modules.Identity.Domain;

/// <summary>
/// A single grantable action, e.g. "students.view". Rows are seeded from
/// docs/01_role_permission_matrix.md — never hardcoded as role checks in application code.
/// </summary>
public class Permission
{
    private Permission() { } // EF Core

    public Permission(string code, string resource, string action)
    {
        Id = Guid.NewGuid();
        Code = code;
        Resource = resource;
        Action = action;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = default!;      // "students.view"
    public string Resource { get; private set; } = default!;  // "students"
    public string Action { get; private set; } = default!;    // "view"
}

public class RolePermission
{
    private RolePermission() { } // EF Core

    public RolePermission(Guid roleId, Guid permissionId, ScopeType defaultScope)
    {
        RoleId = roleId;
        PermissionId = permissionId;
        DefaultScope = defaultScope;
    }

    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
    public ScopeType DefaultScope { get; private set; }
}

/// <summary>Per-user scope override — e.g. narrowing or widening a single permission beyond the role default.</summary>
public class UserPermission
{
    private UserPermission() { } // EF Core

    public UserPermission(Guid userId, Guid permissionId, ScopeType scopeOverride)
    {
        UserId = userId;
        PermissionId = permissionId;
        ScopeOverride = scopeOverride;
    }

    public Guid UserId { get; private set; }
    public Guid PermissionId { get; private set; }
    public ScopeType ScopeOverride { get; private set; }
}

/// <summary>
/// A Super Admin's change to one cell of the role × permission matrix (docs/21.09-audit-jurnali S07.02), layered over the grants that
/// ship in code (<c>PermissionSeedData</c> → <c>role_permissions</c>, which the seeder rewrites on every start — so editing those rows
/// directly would not last). Either a different scope for the role, or <see cref="Revoked"/> — the role loses the permission.
/// <see cref="ScopeType.None"/> is deliberately never used for "no permission": the resolver treats it as a grant.
/// </summary>
public class RolePermissionOverride : TenantEntity
{
    private RolePermissionOverride() { } // EF Core

    public RolePermissionOverride(Guid organizationId, Guid roleId, string roleCode, Guid permissionId, string permissionCode,
        ScopeType? scopeOverride, bool revoked, Guid? updatedBy)
    {
        OrganizationId = organizationId;
        RoleId = roleId;
        RoleCode = roleCode;
        PermissionId = permissionId;
        PermissionCode = permissionCode;
        Set(scopeOverride, revoked, updatedBy);
    }

    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    /// <summary>The codes are kept beside the ids so an audit row reads "SUPPORT_TEACHER · homework.review · MyBranches" rather than two GUIDs.</summary>
    public string RoleCode { get; private set; } = default!;
    public string PermissionCode { get; private set; } = default!;

    public ScopeType? ScopeOverride { get; private set; }
    public bool Revoked { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    public void Set(ScopeType? scopeOverride, bool revoked, Guid? updatedBy)
    {
        ScopeOverride = revoked ? null : scopeOverride;
        Revoked = revoked;
        UpdatedBy = updatedBy;
        MarkUpdated();
    }

    /// <summary>The scope a role ends up with for one permission: the override when there is one (<see cref="Revoked"/> → none), else the
    /// code default. Null means the role does not hold the permission.</summary>
    public static ScopeType? Effective(ScopeType? codeDefault, RolePermissionOverride? layer) =>
        layer is null ? codeDefault : layer.Revoked ? null : layer.ScopeOverride ?? codeDefault;
}
