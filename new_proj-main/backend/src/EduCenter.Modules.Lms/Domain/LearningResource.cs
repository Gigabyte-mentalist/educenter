using EduCenter.SharedKernel;

namespace EduCenter.Modules.Lms.Domain;

/// <summary>
/// Material attached to a lesson: either a link, or a file uploaded to the school's own storage. Teachers were
/// pasting Drive links for the slide deck they had just presented, so the file case is first-class — but the
/// size cap stays low on purpose and anything bigger is still a link, because this storage is a disk on the
/// office machine, not a CDN.
/// </summary>
public class LearningResource : TenantEntity
{
    private LearningResource() { }

    private LearningResource(Guid organizationId, Guid lessonId, Guid createdBy, string title, string? description)
    {
        OrganizationId = organizationId;
        LessonId = lessonId;
        CreatedBy = createdBy;
        Title = title;
        Description = description;
    }

    public static LearningResource ForLink(Guid organizationId, Guid lessonId, Guid createdBy,
        string title, string url, string? description) =>
        new(organizationId, lessonId, createdBy, title, description) { Url = url };

    public static LearningResource ForFile(Guid organizationId, Guid lessonId, Guid createdBy,
        string title, string? description, string storageKey, string fileName, string contentType, long fileSize) =>
        new(organizationId, lessonId, createdBy, title, description)
        {
            StorageKey = storageKey,
            FileName = fileName,
            FileContentType = contentType,
            FileSize = fileSize,
        };

    public Guid LessonId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Set for a link material; null for an uploaded file.</summary>
    public string? Url { get; private set; }

    /// <summary>Set for an uploaded file; null for a link.</summary>
    public string? StorageKey { get; private set; }
    public string? FileName { get; private set; }
    public string? FileContentType { get; private set; }
    public long? FileSize { get; private set; }

    public bool IsFile => StorageKey is not null;
}
