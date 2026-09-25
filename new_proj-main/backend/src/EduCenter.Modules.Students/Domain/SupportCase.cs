using EduCenter.SharedKernel;

namespace EduCenter.Modules.Students.Domain;

public enum SupportCaseStatus
{
    Recommended,
    Assigned,
    Dismissed,
    Resolved
}

public enum SupportCaseSource
{
    /// <summary>Raised by a rule (docs/00_TZ.md §8 "System detects signal").</summary>
    System,

    /// <summary>Proposed by a teacher or administrator from the student's profile.</summary>
    Staff
}

public static class SupportSignalCode
{
    public const string ConsecutiveAbsences = "CONSECUTIVE_ABSENCES";
    public const string HomeworkMissed = "HOMEWORK_MISSED";
    public const string StaffProposal = "STAFF_PROPOSAL";
    public const string WritingGrammarWeak = "WRITING_GRAMMAR_WEAK";
    public const string ListeningLow = "LISTENING_LOW";
    public const string ReadingTime = "READING_TIME";
    public const string SpeakingFluencyLow = "SPEAKING_FLUENCY_LOW";
}

/// <summary>
/// Support Needed Engine (docs/00_TZ.md §8): signal → Support Recommended → Teacher/Admin review →
/// assign a Support Teacher (approval and assignment are one step here) → progress monitored → resolved.
/// A recommendation can also be dismissed at review. Rule-based in Phase 2; AI-detected signals are Phase 4.
/// Scoped through <see cref="GroupId"/> — the group the signal was observed in — so the teachers and
/// support teachers of that group see it, and a branch administrator sees it through the group's branch.
/// </summary>
public class SupportCase : TenantEntity
{
    private SupportCase() { } // EF Core

    public SupportCase(Guid organizationId, Guid studentId, Guid groupId, SupportCaseSource source,
        string signalCode, string reason, Guid? proposedBy)
    {
        OrganizationId = organizationId;
        StudentId = studentId;
        GroupId = groupId;
        Source = source;
        SignalCode = signalCode;
        Reason = reason;
        ProposedBy = proposedBy;
        Status = SupportCaseStatus.Recommended;
    }

    public Guid StudentId { get; private set; }
    public Guid GroupId { get; private set; }
    public SupportCaseSource Source { get; private set; }
    public string SignalCode { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public SupportCaseStatus Status { get; private set; }
    public Guid? ProposedBy { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public Guid? SupportTeacherId { get; private set; }
    public DateTimeOffset? AssignedAt { get; private set; }
    public Guid? ClosedBy { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ResolutionNote { get; private set; }

    public void Assign(Guid supportTeacherId, Guid reviewerId)
    {
        EnsureStatus(SupportCaseStatus.Recommended);
        SupportTeacherId = supportTeacherId;
        ReviewedBy = reviewerId;
        AssignedAt = DateTimeOffset.UtcNow;
        Status = SupportCaseStatus.Assigned;
        MarkUpdated();
    }

    public void Dismiss(Guid reviewerId, string? note)
    {
        EnsureStatus(SupportCaseStatus.Recommended);
        ReviewedBy = reviewerId;
        Close(reviewerId, note, SupportCaseStatus.Dismissed);
    }

    public void Resolve(Guid userId, string? note)
    {
        EnsureStatus(SupportCaseStatus.Assigned);
        Close(userId, note, SupportCaseStatus.Resolved);
    }

    private void Close(Guid userId, string? note, SupportCaseStatus status)
    {
        ClosedBy = userId;
        ClosedAt = DateTimeOffset.UtcNow;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = status;
        MarkUpdated();
    }

    private void EnsureStatus(SupportCaseStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Support case is {Status}, expected {expected}.");
    }
}
