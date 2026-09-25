namespace EduCenter.SharedKernel;

public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; protected set; }

    public void MarkUpdated() => UpdatedAt = DateTimeOffset.UtcNow;
}

public abstract class TenantEntity : BaseEntity
{
    public Guid OrganizationId { get; protected set; }
}

public abstract class BranchScopedEntity : TenantEntity
{
    public Guid BranchId { get; protected set; }
}
