using EduCenter.SharedKernel;

namespace EduCenter.Modules.Students.Domain;

/// <summary>docs/00_TZ.md §10 — the post-enrollment portion of the lifecycle (a Lead becomes a
/// Student, with a login User account, only at conversion).</summary>
public enum StudentStatus
{
    Active,
    Debt,
    Frozen,
    Transferred,
    Graduated,
    Dropped
}

public class Student : TenantEntity
{
    private Student() { } // EF Core

    public Student(Guid organizationId, Guid userId, string studentNumber, DateOnly joinedDate, string? currentLevel)
    {
        OrganizationId = organizationId;
        UserId = userId;
        StudentNumber = studentNumber;
        JoinedDate = joinedDate;
        CurrentLevel = currentLevel;
        Status = StudentStatus.Active;
    }

    public Guid UserId { get; private set; }
    public string StudentNumber { get; private set; } = default!;
    public DateOnly JoinedDate { get; private set; }
    public string? CurrentLevel { get; private set; }
    public StudentStatus Status { get; private set; }

    public void SetStatus(StudentStatus status)
    {
        Status = status;
        MarkUpdated();
    }

    public void UpdateProfile(DateOnly joinedDate, string? currentLevel)
    {
        JoinedDate = joinedDate;
        CurrentLevel = currentLevel;
        MarkUpdated();
    }

    /// <summary>Centres arriving from another system keep the ids their families already know.</summary>
    public void Renumber(string studentNumber)
    {
        StudentNumber = studentNumber;
        MarkUpdated();
    }
}
