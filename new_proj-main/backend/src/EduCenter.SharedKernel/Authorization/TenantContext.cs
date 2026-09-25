namespace EduCenter.SharedKernel.Authorization;

/// <summary>
/// Per-request identity + tenant context, resolved from the JWT claims by the API layer
/// and consumed by module authorization handlers to evaluate <see cref="ScopeType"/>.
/// </summary>
public interface ITenantContext
{
    Guid UserId { get; }
    Guid OrganizationId { get; }
    IReadOnlyCollection<Guid> BranchIds { get; }
    IReadOnlyCollection<string> RoleCodes { get; }
}

public enum ScopeType
{
    AllOrganization,
    MyBranches,
    MyGroups,
    AssignedGroups,
    Self,
    MyChildren,
    None
}
