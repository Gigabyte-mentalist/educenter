using EduCenter.SharedKernel;

namespace EduCenter.Modules.Notifications.Domain;

/// <summary>Event codes of the Notification Engine (docs/00_TZ.md §39). Stored as strings so a new type needs no migration.</summary>
public static class NotificationType
{
    public const string PaymentDue = "PAYMENT_DUE";
    public const string PaymentOverdue = "PAYMENT_OVERDUE";
    public const string Absent = "ABSENT";
    public const string AbsenceStreak = "ABSENCE_STREAK";
    public const string ExamResult = "EXAM_RESULT";
    public const string HomeworkCreated = "HOMEWORK_CREATED";
    public const string HomeworkDeadline = "HOMEWORK_DEADLINE";
    public const string HomeworkReviewed = "HOMEWORK_REVIEWED";
    public const string HomeworkChanged = "HOMEWORK_CHANGED";
    public const string LessonReminder = "LESSON_REMINDER";
    public const string ScheduleChanged = "SCHEDULE_CHANGED";
    public const string TeacherAssigned = "TEACHER_ASSIGNED";
    public const string SupportRecommended = "SUPPORT_RECOMMENDED";
    public const string LearningSignal = "LEARNING_SIGNAL";
    public const string SupportAssigned = "SUPPORT_ASSIGNED";
    public const string TaskAssigned = "TASK_ASSIGNED";
    public const string WritingEvaluated = "WRITING_EVALUATED";
    public const string SpeakingReviewed = "SPEAKING_REVIEWED";
    public const string Transferred = "TRANSFERRED";
    public const string CourseFrozen = "COURSE_FROZEN";
    public const string TeacherReplaced = "TEACHER_REPLACED";
    public const string ParentLinked = "PARENT_LINKED";
    public const string Enrolled = "ENROLLED";
    public const string EnrollmentDropped = "ENROLLMENT_DROPPED";
    public const string Graduated = "GRADUATED";
    public const string GroupClosed = "GROUP_CLOSED";
    public const string CredentialSubmitted = "CREDENTIAL_SUBMITTED";
    public const string CredentialVerified = "CREDENTIAL_VERIFIED";
    public const string Announcement = "ANNOUNCEMENT";
    public const string ComplaintCreated = "COMPLAINT_CREATED";
    public const string ComplaintAssigned = "COMPLAINT_ASSIGNED";
    public const string ComplaintResolved = "COMPLAINT_RESOLVED";
}

/// <summary>Where a notification can reach a person (docs/00_TZ.md §39). Stored as text, so a new channel needs no migration.
/// Push is Phase 5; Telegram waits for a bot token.</summary>
public enum NotificationChannel
{
    InApp,
    Telegram,
    Email,
    Sms
}

/// <summary>
/// One message in one user's inbox. <see cref="DedupeKey"/> identifies what the message is about for reminders
/// produced by a timer (e.g. "payment-due:{invoiceId}"), so the same reminder is never delivered twice.
/// <see cref="Link"/> is an in-app path; the body never carries more than the recipient may already see.
/// </summary>
public class Notification : TenantEntity
{
    private Notification() { } // EF Core

    public Notification(Guid organizationId, Guid userId, string type, string title, string body, string? link, string? dedupeKey)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Type = type;
        Title = title;
        Body = body;
        Link = link;
        DedupeKey = dedupeKey;
    }

    public Guid UserId { get; private set; }
    public string Type { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public string? Link { get; private set; }
    public string? DedupeKey { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead()
    {
        if (ReadAt is not null) return;
        ReadAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>A user's opt-out (or opt-in) for one notification type on one channel. No row means the default: enabled.</summary>
public class NotificationPreference : TenantEntity
{
    private NotificationPreference() { } // EF Core

    public NotificationPreference(Guid organizationId, Guid userId, string type, NotificationChannel channel, bool enabled)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Type = type;
        Channel = channel;
        Enabled = enabled;
    }

    public Guid UserId { get; private set; }
    public string Type { get; private set; } = default!;
    public NotificationChannel Channel { get; private set; }
    public bool Enabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        MarkUpdated();
    }
}

public enum DeliveryStatus
{
    Pending,
    Sent,
    /// <summary>Every attempt failed; the inbox message is still there.</summary>
    Failed,
    /// <summary>Deliberately not sent: the channel is off, the person has no address, or the daily SMS allowance is spent.</summary>
    Skipped
}

/// <summary>
/// One message to one person on one outside channel (Email, SMS). The inbox row is the message; this row is the attempt to also
/// tell the person elsewhere — with its own status, so a failing mail server never touches the inbox, and a report can say what was sent.
/// It carries its own text: outside the app only a generic title and a link go out (docs/00_TZ.md §23), never the amount or the name
/// the inbox message holds. <see cref="NotificationId"/> is null when the person muted the inbox but kept the channel.
/// </summary>
public class NotificationDelivery : TenantEntity
{
    public const int MaxAttempts = 3;

    private NotificationDelivery() { } // EF Core

    public NotificationDelivery(Guid organizationId, Guid userId, Guid? notificationId, NotificationChannel channel, string type,
        string subject, string body, string? dedupeKey)
    {
        OrganizationId = organizationId;
        UserId = userId;
        NotificationId = notificationId;
        Channel = channel;
        Type = type;
        Subject = subject;
        Body = body;
        DedupeKey = dedupeKey;
        Status = DeliveryStatus.Pending;
    }

    public Guid UserId { get; private set; }
    public Guid? NotificationId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Type { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public string? DedupeKey { get; private set; }
    public DeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public bool IsPending => Status == DeliveryStatus.Pending;

    public void RecordAttempt()
    {
        Attempts++;
        MarkUpdated();
    }

    public void MarkSent()
    {
        Status = DeliveryStatus.Sent;
        Error = null;
        SentAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    /// <summary>Keeps the reason and stays <see cref="DeliveryStatus.Pending"/> until the last attempt; then it is <see cref="DeliveryStatus.Failed"/>.</summary>
    public void RecordFailure(string error)
    {
        Error = Clip(error);
        if (Attempts >= MaxAttempts) Status = DeliveryStatus.Failed;
        MarkUpdated();
    }

    public void Skip(string reason)
    {
        Status = DeliveryStatus.Skipped;
        Error = Clip(reason);
        MarkUpdated();
    }

    private static string Clip(string text) => text.Length <= 200 ? text : text[..199] + "…";
}
