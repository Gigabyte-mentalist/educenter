using EduCenter.SharedKernel.Authorization;

namespace EduCenter.Api.Authorization;

public static class ScopeFilterExtensions
{
    /// <summary>Reads the ResolvedScope the PermissionAuthorizationHandler stashed for this request.</summary>
    public static ResolvedScope RequireResolvedScope(this HttpContext httpContext) =>
        httpContext.Items["ResolvedScope"] as ResolvedScope
        ?? throw new InvalidOperationException(
            "No ResolvedScope on HttpContext — the endpoint is missing an [Authorize(Policy = \"...\")] permission check.");

    /// <summary>Branch-scoped queries: AllOrganization sees every branch id passed in, MyBranches is
    /// restricted to the caller's assigned branches, everything else denies (no branch-level data to see).</summary>
    public static IReadOnlyCollection<Guid>? AllowedBranchIds(this ResolvedScope scope, IReadOnlyCollection<Guid> allOrgBranchIds) =>
        scope.Scope switch
        {
            ScopeType.AllOrganization => allOrgBranchIds,
            ScopeType.MyBranches => scope.BranchIds,
            _ => null // caller has no branch-level visibility for this resource
        };
}
