using EduCenter.SharedKernel;

namespace EduCenter.Modules.Lms.Domain;

/// <summary>docs/00_TZ.md §16.</summary>
public enum HomeworkType
{
    Writing,
    Speaking,
    Reading,
    Listening,
    Grammar,
    Vocabulary,
    FileUpload,
    Quiz,
    Custom
}

public class Homework : TenantEntity
{
    private Homework() { } // EF Core

    public Homework(Guid organizationId, Guid lessonId, string title, string? description,
        HomeworkType type, DateTimeOffset deadline, int maxScore)
    {
        OrganizationId = organizationId;
        LessonId = lessonId;
        Title = title;
        Description = description;
        Type = type;
        Deadline = deadline;
        MaxScore = maxScore;
    }

    public Guid LessonId { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }
    public HomeworkType Type { get; private set; }
    public DateTimeOffset Deadline { get; private set; }
    public int MaxScore { get; private set; }

    /// <summary>The teacher's own file for the task — a worksheet, an audio clip. Null when the homework is text only.</summary>
    public string? AttachmentKey { get; private set; }
    public string? AttachmentName { get; private set; }
    public string? AttachmentContentType { get; private set; }
    public long? AttachmentSize { get; private set; }

    public bool HasAttachment => AttachmentKey is not null;

    public void AttachFile(string key, string name, string contentType, long size)
    {
        AttachmentKey = key;
        AttachmentName = name;
        AttachmentContentType = contentType;
        AttachmentSize = size;
        MarkUpdated();
    }

    public void Update(string title, string? description, HomeworkType type, DateTimeOffset deadline, int maxScore)
    {
        Title = title;
        Description = description;
        Type = type;
        Deadline = deadline;
        MaxScore = maxScore;
        MarkUpdated();
    }
}

public class HomeworkSubmission : TenantEntity
{
    private HomeworkSubmission() { } // EF Core

    public HomeworkSubmission(Guid organizationId, Guid homeworkId, Guid studentId, string? contentUrl, string? contentText)
    {
        OrganizationId = organizationId;
        HomeworkId = homeworkId;
        StudentId = studentId;
        ContentUrl = contentUrl;
        ContentText = contentText;
        SubmittedAt = DateTimeOffset.UtcNow;
    }

    public Guid HomeworkId { get; private set; }
    public Guid StudentId { get; private set; }
    public string? ContentUrl { get; private set; }
    public string? ContentText { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public int? Score { get; private set; }
    public string? Feedback { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>The student's answer as a file — alongside the text, or instead of it. Null when they wrote or linked only.</summary>
    public string? AttachmentKey { get; private set; }
    public string? AttachmentName { get; private set; }
    public string? AttachmentContentType { get; private set; }
    public long? AttachmentSize { get; private set; }

    public bool HasAttachment => AttachmentKey is not null;

    public void AttachFile(string key, string name, string contentType, long size)
    {
        AttachmentKey = key;
        AttachmentName = name;
        AttachmentContentType = contentType;
        AttachmentSize = size;
    }

    public void Review(int score, string? feedback, Guid reviewerId)
    {
        Score = score;
        Feedback = feedback;
        ReviewedBy = reviewerId;
        ReviewedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }
}

/// <summary>One student an individual homework was given to (docs/00_TZ.md §16, "group/student assignment"). A homework with no
/// rows here goes to the whole group — every homework that existed before this table did, and every one created without a list of
/// students. Counting who was assigned what goes through <c>HomeworkQueries</c>, never through the group's roster alone.</summary>
public class HomeworkTarget : TenantEntity
{
    private HomeworkTarget() { } // EF Core

    public HomeworkTarget(Guid organizationId, Guid homeworkId, Guid studentId)
    {
        OrganizationId = organizationId;
        HomeworkId = homeworkId;
        StudentId = studentId;
    }

    public Guid HomeworkId { get; private set; }
    public Guid StudentId { get; private set; }
}
