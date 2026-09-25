using EduCenter.SharedKernel;

namespace EduCenter.Modules.Crm.Domain;

public enum CrmTaskType
{
    FollowUp,
    Debt,
    Absence,
    CallBack
}

/// <summary>Machine-readable origin of an automatically created task — also the key the follow-up
/// rules use to avoid creating the same task twice.</summary>
public static class CrmTaskReason
{
    public const string TrialFollowUp = "TRIAL_FOLLOW_UP";
    public const string ConsecutiveAbsences = "CONSECUTIVE_ABSENCES";
    public const string DebtOverdue = "DEBT_OVERDUE";
}

public enum CrmTaskStatus
{
    Open,
    Done,
    Cancelled
}

/// <summary>Backs the Operator's "Today's Tasks / Call Queue" (docs/00_TZ.md §3.3). Created either
/// manually or automatically — e.g. picking CallOutcome.CallLater on a CrmCall creates one.</summary>
public class CrmTask : TenantEntity
{
    private CrmTask() { } // EF Core

    public CrmTask(Guid organizationId, Guid? leadId, Guid? studentId, Guid assignedTo,
        CrmTaskType type, DateTimeOffset dueAt, string? reasonCode = null, string? note = null)
    {
        if (leadId is null && studentId is null)
            throw new ArgumentException("A CrmTask must reference either a lead or a student.");

        OrganizationId = organizationId;
        LeadId = leadId;
        StudentId = studentId;
        AssignedTo = assignedTo;
        Type = type;
        DueAt = dueAt;
        ReasonCode = reasonCode;
        Note = note;
        Status = CrmTaskStatus.Open;
    }

    public Guid? LeadId { get; private set; }
    public Guid? StudentId { get; private set; }
    public Guid AssignedTo { get; private set; }
    public CrmTaskType Type { get; private set; }
    public CrmTaskStatus Status { get; private set; }
    public DateTimeOffset DueAt { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? Note { get; private set; }

    public void Complete()
    {
        Status = CrmTaskStatus.Done;
        MarkUpdated();
    }

    public void Cancel()
    {
        Status = CrmTaskStatus.Cancelled;
        MarkUpdated();
    }
}
