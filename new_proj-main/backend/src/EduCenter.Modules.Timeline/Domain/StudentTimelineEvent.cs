using EduCenter.SharedKernel;

namespace EduCenter.Modules.Timeline.Domain;

/// <summary>docs/00_TZ.md §9 — merges CRM and academic history into one chronological view per student.
/// Written directly by the controllers that cause these events (Sprint 4 scope); a proper domain-event
/// dispatcher is a reasonable later refactor once more event sources exist across modules.</summary>
public static class TimelineEventType
{
    public const string Enrolled = "ENROLLED";
    public const string LessonAttended = "LESSON_ATTENDED";
    public const string LessonAbsent = "LESSON_ABSENT";
    public const string HomeworkSubmitted = "HOMEWORK_SUBMITTED";
    public const string HomeworkReviewed = "HOMEWORK_REVIEWED";
    public const string ExamResultRecorded = "EXAM_RESULT_RECORDED";
    /// <summary>A result that was entered wrongly and taken back — the timeline keeps saying it happened.</summary>
    public const string ExamResultRemoved = "EXAM_RESULT_REMOVED";
    public const string SupportRecommended = "SUPPORT_RECOMMENDED";
    public const string SupportAssigned = "SUPPORT_ASSIGNED";
    public const string SupportDismissed = "SUPPORT_DISMISSED";
    public const string SupportResolved = "SUPPORT_RESOLVED";
    public const string PaymentReceived = "PAYMENT_RECEIVED";
    public const string PaymentVoided = "PAYMENT_VOIDED";
    public const string DiscountApplied = "DISCOUNT_APPLIED";
    public const string DiscountRevoked = "DISCOUNT_REVOKED";
    /// <summary>An invoice was cut into instalments, each with its own due date (S07.05).</summary>
    public const string InvoiceSplit = "INVOICE_SPLIT";
    public const string RefundIssued = "REFUND_ISSUED";
    public const string WritingEvaluated = "WRITING_EVALUATED";
    public const string WritingOverridden = "WRITING_OVERRIDDEN";
    public const string SpeakingReviewed = "SPEAKING_REVIEWED";

    // docs/00_TZ.md §20-21 — the two lifecycle moves a centre makes every week.
    public const string Transferred = "TRANSFERRED";
    public const string CourseFrozen = "COURSE_FROZEN";
    public const string CourseResumed = "COURSE_RESUMED";
    public const string TeacherReplaced = "TEACHER_REPLACED";

    // docs/00_TZ.md §24 — a guardian account linked to, or unlinked from, a student.
    public const string ParentLinked = "PARENT_LINKED";
    public const string ParentUnlinked = "PARENT_UNLINKED";

    // docs/00_TZ.md §10 — the lifecycle's final moves: leaving a group without transferring, and finishing the course.
    public const string EnrollmentDropped = "ENROLLMENT_DROPPED";
    public const string Graduated = "GRADUATED";

    // docs/00_TZ.md §41 — a complaint about (or from the family of) this student. The timeline is read by the student's own teachers, the student and
    // the parents too, so only the staff who handle complaints are shown these two (ComplaintEvents); the payload never carries the text.
    public const string ComplaintCreated = "COMPLAINT_CREATED";
    public const string ComplaintResolved = "COMPLAINT_RESOLVED";

    /// <summary>Events that only the people who handle complaints may see on a student's timeline.</summary>
    public static readonly string[] ComplaintEvents = [ComplaintCreated, ComplaintResolved];
}

public class StudentTimelineEvent : TenantEntity
{
    private StudentTimelineEvent() { } // EF Core

    public StudentTimelineEvent(Guid organizationId, Guid studentId, string eventType, string? payloadJson)
    {
        OrganizationId = organizationId;
        StudentId = studentId;
        EventType = eventType;
        PayloadJson = payloadJson;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    public Guid StudentId { get; private set; }
    public string EventType { get; private set; } = default!;
    public string? PayloadJson { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}
