using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace EduCenter.Api.Authorization;

/// <summary>
/// Resolves whether the caller holds <see cref="PermissionRequirement.PermissionCode"/> and, if so,
/// what ScopeType applies (role default unless a per-user UserPermission override exists — see
/// docs/01_role_permission_matrix.md §1 and §4 rule 1: "View permission va Action permission alohida").
/// The resolved scope is stashed on HttpContext.Items for the endpoint to use when filtering data.
/// </summary>
public class PermissionAuthorizationHandler(PermissionResolver permissions, IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            return;

        var resolved = await permissions.ResolveAsync(userId, requirement.PermissionCode);
        if (resolved is null)
            return; // no grant for this permission code — deny

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
            httpContext.Items["ResolvedScope"] = resolved;

        context.Succeed(requirement);
    }
}
