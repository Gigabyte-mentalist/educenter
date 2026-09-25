using EduCenter.SharedKernel;

namespace EduCenter.Modules.Audit.Domain;

/// <summary>WHO/WHAT/WHEN/WHERE/OLD/NEW record per docs/00_TZ.md §52. Append-only.</summary>
public class AuditLog : TenantEntity
{
    private AuditLog() { } // EF Core

    public AuditLog(
        Guid organizationId,
        Guid userId,
        string action,
        string resource,
        Guid resourceId,
        string? oldValueJson,
        string? newValueJson,
        string? reason)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Action = action;
        Resource = resource;
        ResourceId = resourceId;
        OldValueJson = oldValueJson;
        NewValueJson = newValueJson;
        Reason = reason;
    }

    public Guid UserId { get; private set; }
    public string Action { get; private set; } = default!;
    public string Resource { get; private set; } = default!;
    public Guid ResourceId { get; private set; }
    public string? OldValueJson { get; private set; }
    public string? NewValueJson { get; private set; }
    public string? Reason { get; private set; }
}
