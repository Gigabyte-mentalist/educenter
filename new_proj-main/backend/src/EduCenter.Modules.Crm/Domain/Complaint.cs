using EduCenter.SharedKernel;

namespace EduCenter.Modules.Crm.Domain;

/// <summary>Where a complaint stands. The order is the road: a complaint only ever moves forward, so a later status is a bigger number.</summary>
public enum ComplaintStatus
{
    New = 0,
    Assigned = 1,
    InProgress = 2,
    Resolved = 3,

    /// <summary>The author has rated the outcome (or the branch closed it without waiting for a rating).</summary>
    Closed = 4
}

/// <summary>
/// A client's dissatisfaction (docs/00_TZ.md §41): <c>Complaint → Assigned → In Progress → Resolved → Feedback</c>. Not a
/// <c>SupportCase</c> — that is a signal that a student is struggling with the material; this is somebody telling the centre it got
/// something wrong. It belongs to the branch it is about, and to the student it concerns when there is one (then it also shows on
/// that student's timeline for the staff who handle complaints).
/// </summary>
public class Complaint : BranchScopedEntity
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 4000;
    public const int MaxResolutionLength = 2000;
    public const int MaxFeedbackLength = 1000;
    public const int MaxScore = 10;

    private Complaint() { } // EF Core

    public Complaint(Guid organizationId, Guid branchId, Guid? studentId, Guid authorUserId, string subject, string body)
    {
        OrganizationId = organizationId;
        BranchId = branchId;
        StudentId = studentId;
        AuthorUserId = authorUserId;
        Subject = subject;
        Body = body;
        Status = ComplaintStatus.New;
    }

    public Guid? StudentId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public ComplaintStatus Status { get; private set; }
    public Guid? AssignedToUserId { get; private set; }
    public string? Resolution { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>How satisfied the author is with the outcome, 0–10 (docs/00_TZ.md §41 "Feedback"); the same scale the NPS uses.</summary>
    public int? SatisfactionScore { get; private set; }

    public string? FeedbackComment { get; private set; }

    /// <summary>Hands the complaint to a member of staff. May be repeated while it is open (a different person takes over), which does not
    /// change where it stands — only the first assignment moves it from New to Assigned.</summary>
    public void Assign(Guid userId)
    {
        if (Status is ComplaintStatus.Resolved or ComplaintStatus.Closed)
            throw new InvalidOperationException($"Complaint is {Status} and can no longer be assigned.");

        AssignedToUserId = userId;
        if (Status == ComplaintStatus.New) Status = ComplaintStatus.Assigned;
        MarkUpdated();
    }

    public void Start()
    {
        EnsureStatus(ComplaintStatus.Assigned);
        Status = ComplaintStatus.InProgress;
        MarkUpdated();
    }

    /// <summary>Records what was done about it. Allowed from Assigned as well as InProgress: starting is a courtesy, not a gate.</summary>
    public void Resolve(string resolution)
    {
        if (Status is not (ComplaintStatus.Assigned or ComplaintStatus.InProgress))
            throw new InvalidOperationException($"Complaint is {Status}, expected Assigned or InProgress.");

        Resolution = resolution;
        ResolvedAt = DateTimeOffset.UtcNow;
        Status = ComplaintStatus.Resolved;
        MarkUpdated();
    }

    /// <summary>The author's verdict on the outcome; it closes the complaint.</summary>
    public void GiveFeedback(int score, string? comment)
    {
        EnsureStatus(ComplaintStatus.Resolved);
        if (score is < 0 or > MaxScore) throw new ArgumentOutOfRangeException(nameof(score), $"Score is 0–{MaxScore}.");

        SatisfactionScore = score;
        FeedbackComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        Status = ComplaintStatus.Closed;
        MarkUpdated();
    }

    /// <summary>Closes a resolved complaint whose author never answered, so it does not sit in "resolved" for good.</summary>
    public void Close()
    {
        EnsureStatus(ComplaintStatus.Resolved);
        Status = ComplaintStatus.Closed;
        MarkUpdated();
    }

    private void EnsureStatus(ComplaintStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException($"Complaint is {Status}, expected {expected}.");
    }
}
