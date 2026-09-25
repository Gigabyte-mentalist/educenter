using EduCenter.SharedKernel;

namespace EduCenter.Modules.Students.Domain;

public enum EnrollmentStatus
{
    Active,
    Frozen,
    Transferred,
    Dropped,
    Completed
}

/// <summary>Ties a Student to an Academic.Group. Referenced by Guid only (no C# type reference) —
/// modules stay decoupled the same way Phase 1's Academic/Identity modules do.</summary>
public class Enrollment : TenantEntity
{
    private Enrollment() { } // EF Core

    public Enrollment(Guid organizationId, Guid studentId, Guid groupId)
    {
        OrganizationId = organizationId;
        StudentId = studentId;
        GroupId = groupId;
        EnrolledAt = DateTimeOffset.UtcNow;
        Status = EnrollmentStatus.Active;
    }

    public Guid StudentId { get; private set; }
    public Guid GroupId { get; private set; }
    public DateTimeOffset EnrolledAt { get; private set; }
    public EnrollmentStatus Status { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public string? EndReason { get; private set; }

    public void SetStatus(EnrollmentStatus status)
    {
        Status = status;
        MarkUpdated();
    }

    /// <summary>A final move: <see cref="Dropped"/>, <see cref="Completed"/> or <see cref="Transferred"/> — each
    /// leaves this row behind rather than resuming it, so it is worth dating and, for a drop, worth a reason.
    /// Frozen is not one of these: it is meant to resume, so it goes through <see cref="SetStatus"/> instead.</summary>
    public void End(EnrollmentStatus status, string? reason)
    {
        Status = status;
        EndedAt = DateTimeOffset.UtcNow;
        EndReason = reason;
        MarkUpdated();
    }
}
