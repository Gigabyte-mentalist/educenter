using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace EduCenter.Api.Authorization;

/// <summary>
/// One requirement per permission code, e.g. "students.view". The handler resolves the caller's
/// effective ScopeType for that permission (role default, narrowed/widened by UserPermission override)
/// and stores it on HttpContext.Items so controllers/handlers can filter queries by it.
/// </summary>
public class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}

/// <summary>Resolved scope for the permission the current request needed — read by query handlers
/// to build the actual data filter (e.g. WHERE branch_id IN (...) for MyBranches).</summary>
public class ResolvedScope
{
    public required ScopeType Scope { get; init; }
    public required Guid UserId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required IReadOnlyCollection<Guid> BranchIds { get; init; }
}
