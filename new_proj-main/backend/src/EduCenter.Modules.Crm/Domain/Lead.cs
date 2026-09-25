using EduCenter.SharedKernel;

namespace EduCenter.Modules.Crm.Domain;

/// <summary>docs/00_TZ.md §10 — the pre-enrollment portion of the lifecycle. A Lead has no login
/// account; it becomes a Student (with a User account) only at conversion (Sprint 2).</summary>
public enum LeadStatus
{
    Lead,
    Contacted,
    TrialBooked,
    TrialAttended,
    TrialMissed,
    NotInterested,
    Lost,
    Converted
}

public enum LeadPriority
{
    Normal,
    High
}

public class Lead : BranchScopedEntity
{
    private Lead() { } // EF Core

    public Lead(Guid organizationId, Guid branchId, string fullName, string phone, string source,
        string interestedCourse, Guid operatorId)
    {
        OrganizationId = organizationId;
        BranchId = branchId;
        FullName = fullName;
        Phone = phone;
        Source = source;
        InterestedCourse = interestedCourse;
        OperatorId = operatorId;
        Status = LeadStatus.Lead;
    }

    public string FullName { get; private set; } = default!;
    public string Phone { get; private set; } = default!;
    public string Source { get; private set; } = default!;
    public string InterestedCourse { get; private set; } = default!;
    public Guid OperatorId { get; private set; }
    public LeadStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public string? LostReason { get; private set; }
    public Guid? ConvertedStudentId { get; private set; }
    public LeadPriority Priority { get; private set; } = LeadPriority.Normal;

    /// <summary>When the person can attend, as comma-separated <see cref="PreferredTimeCodes"/> ("Evening,Weekend"). Plain text on
    /// purpose — a text-backed array column is an EF quirk we avoid. Null = no preference.</summary>
    public string? PreferredTime { get; private set; }

    /// <summary>The level the person says they are at (free text, "B1", "IELTS Foundation"). The placement test only <i>suggests</i>
    /// a level; the operator confirms it here.</summary>
    public string? CurrentLevel { get; private set; }

    /// <summary>Both values are already normalised by the caller; null clears.</summary>
    public void UpdatePreferences(string? preferredTime, string? currentLevel)
    {
        PreferredTime = preferredTime;
        CurrentLevel = currentLevel;
        MarkUpdated();
    }

    public void SetStatus(LeadStatus status)
    {
        Status = status;
        MarkUpdated();
    }

    public void MarkLost(string reason)
    {
        Status = LeadStatus.Lost;
        LostReason = reason;
        MarkUpdated();
    }

    /// <summary>Raised by the trial follow-up rule so the lead rises to the top of the operator's list.</summary>
    public void RaisePriority()
    {
        if (Priority == LeadPriority.High) return;
        Priority = LeadPriority.High;
        MarkUpdated();
    }

    public void MarkConverted(Guid studentId)
    {
        Status = LeadStatus.Converted;
        ConvertedStudentId = studentId;
        MarkUpdated();
    }

    public void AppendNote(string note)
    {
        Notes = Notes is null ? note : $"{Notes}\n{note}";
        MarkUpdated();
    }
}
