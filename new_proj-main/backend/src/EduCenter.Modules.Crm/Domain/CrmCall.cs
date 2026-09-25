using EduCenter.SharedKernel;

namespace EduCenter.Modules.Crm.Domain;

/// <summary>docs/00_TZ.md §3.3 — the fixed set of outcomes an operator picks after every call.</summary>
public enum CallOutcome
{
    Answered,
    NoAnswer,
    CallLater,
    Interested,
    NotInterested,
    PaymentPromised,
    TrialBooked,
    WrongNumber,
    Transferred,
    Lost
}

public class CrmCall : TenantEntity
{
    private CrmCall() { } // EF Core

    public CrmCall(Guid organizationId, Guid leadId, Guid operatorId, CallOutcome outcome, string? notes)
    {
        OrganizationId = organizationId;
        LeadId = leadId;
        OperatorId = operatorId;
        Outcome = outcome;
        Notes = notes;
        CalledAt = DateTimeOffset.UtcNow;
    }

    public Guid LeadId { get; private set; }
    public Guid OperatorId { get; private set; }
    public CallOutcome Outcome { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CalledAt { get; private set; }
}
