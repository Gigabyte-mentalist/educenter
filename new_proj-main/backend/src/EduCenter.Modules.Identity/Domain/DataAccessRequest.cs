using EduCenter.SharedKernel;

namespace EduCenter.Modules.Identity.Domain;

public enum DataAccessStatus
{
    Pending,
    Approved,
    Rejected,
    Revoked
}

/// <summary>
/// Temporary access to production data (docs/00_TZ.md §3.8, matrix §3.12 <c>production.data.request</c>). A Developer holds no
/// student data by default; when a fault can only be found in real rows they ask, give a reason, and an organization-wide
/// approver grants one read permission for a limited time. Request → Reason → Approval → Expiration, and every step is
/// audited because this entity is in the audit log's scope.
/// </summary>
public class DataAccessRequest : TenantEntity
{
    public const int MinReasonLength = 20;
    public const int MaxReasonLength = 1000;
    public const int MaxHours = 72;

    /// <summary>What may be requested: read access to academic records. Finance, accounts and permissions are never on this
    /// list — a bug in a payment is diagnosed from the invoice ids in the logs, not by browsing everyone's money.</summary>
    public static readonly IReadOnlyList<string> GrantableCodes =
        ["students.view", "groups.view", "attendance.view", "exams.view", "homework.view"];

    private DataAccessRequest() { } // EF Core

    public DataAccessRequest(Guid organizationId, Guid requesterId, string permissionCode, string reason, int hours)
    {
        OrganizationId = organizationId;
        RequesterId = requesterId;
        PermissionCode = permissionCode;
        Reason = reason.Trim();
        Hours = hours;
        Status = DataAccessStatus.Pending;
    }

    public Guid RequesterId { get; private set; }
    public string PermissionCode { get; private set; } = default!;
    public string Reason { get; private set; } = default!;

    /// <summary>How long the requester asked for; the approver may grant less (or more, up to <see cref="MaxHours"/>).</summary>
    public int Hours { get; private set; }

    public DataAccessStatus Status { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => Status == DataAccessStatus.Approved && ExpiresAt > now;

    public void Approve(Guid approverId, int hours, string? note, DateTimeOffset now)
    {
        RequirePending();
        Status = DataAccessStatus.Approved;
        Decide(approverId, note, now);
        ExpiresAt = now.AddHours(Math.Clamp(hours, 1, MaxHours));
    }

    public void Reject(Guid approverId, string? note, DateTimeOffset now)
    {
        RequirePending();
        Status = DataAccessStatus.Rejected;
        Decide(approverId, note, now);
    }

    /// <summary>Ends an approved grant before its expiry.</summary>
    public void Revoke(Guid approverId, string? note, DateTimeOffset now)
    {
        if (!IsActive(now)) throw new InvalidOperationException("Faqat amaldagi ruxsatni bekor qilish mumkin.");
        Status = DataAccessStatus.Revoked;
        Decide(approverId, note, now);
        ExpiresAt = now;
    }

    private void RequirePending()
    {
        if (Status != DataAccessStatus.Pending) throw new InvalidOperationException("So'rov allaqachon ko'rib chiqilgan.");
    }

    private void Decide(Guid approverId, string? note, DateTimeOffset now)
    {
        DecidedBy = approverId;
        DecidedAt = now;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        MarkUpdated();
    }
}
