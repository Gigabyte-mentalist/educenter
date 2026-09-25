using EduCenter.SharedKernel;

namespace EduCenter.Modules.Students.Domain;

/// <summary>
/// A file attached to a student's own profile — a passport scan, a signed contract, a payment receipt —
/// as opposed to <c>LearningResource</c>, which attaches to a lesson. Mirrors that same link-or-file shape
/// for the same reason: paperwork often already lives in Drive, and re-uploading it would just be a second
/// copy to keep in sync.
/// </summary>
public class StudentDocument : TenantEntity
{
    private StudentDocument() { }

    private StudentDocument(Guid organizationId, Guid studentId, Guid uploadedBy, string title, string? description)
    {
        OrganizationId = organizationId;
        StudentId = studentId;
        UploadedBy = uploadedBy;
        Title = title;
        Description = description;
    }

    public static StudentDocument ForLink(Guid organizationId, Guid studentId, Guid uploadedBy,
        string title, string url, string? description) =>
        new(organizationId, studentId, uploadedBy, title, description) { Url = url };

    public static StudentDocument ForFile(Guid organizationId, Guid studentId, Guid uploadedBy,
        string title, string? description, string storageKey, string fileName, string contentType, long fileSize) =>
        new(organizationId, studentId, uploadedBy, title, description)
        {
            StorageKey = storageKey,
            FileName = fileName,
            FileContentType = contentType,
            FileSize = fileSize,
        };

    public Guid StudentId { get; private set; }
    public Guid UploadedBy { get; private set; }
    public string Title { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Set for a link document; null for an uploaded file.</summary>
    public string? Url { get; private set; }

    /// <summary>Set for an uploaded file; null for a link.</summary>
    public string? StorageKey { get; private set; }
    public string? FileName { get; private set; }
    public string? FileContentType { get; private set; }
    public long? FileSize { get; private set; }

    public bool IsFile => StorageKey is not null;
}
