using EduCenter.SharedKernel;

namespace EduCenter.Modules.Notifications.Domain;

/// <summary>Who an announcement is for. Stored as text.</summary>
public enum AnnouncementAudience
{
    All,
    Students,
    Parents,
    Staff
}

/// <summary>
/// A notice from the centre to the people it concerns (docs/00_TZ.md §24 "announcements"): a holiday, a schedule change, a new course. Where it applies
/// is <see cref="BranchId"/> and <see cref="GroupId"/> — both empty means the whole organization, a branch means everyone attached to it, a group
/// means that group's students, parents and teachers. Once published only the wording, the audience and the expiry can change; to move it somewhere
/// else, delete it and publish again (the people it was sent to have already been told).
/// </summary>
public class Announcement : TenantEntity
{
    private Announcement() { } // EF Core

    public Announcement(Guid organizationId, Guid? branchId, Guid? groupId, string title, string body,
        AnnouncementAudience audience, Guid authorId, DateTimeOffset? expiresAt)
    {
        OrganizationId = organizationId;
        BranchId = branchId;
        GroupId = groupId;
        Title = title;
        Body = body;
        Audience = audience;
        AuthorId = authorId;
        PublishedAt = DateTimeOffset.UtcNow;
        ExpiresAt = expiresAt;
    }

    /// <summary>Set for a branch's announcement and for a group's (the group's branch); empty for the whole organization.</summary>
    public Guid? BranchId { get; private set; }

    public Guid? GroupId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public AnnouncementAudience Audience { get; private set; }
    public Guid AuthorId { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }

    /// <summary>After this instant nobody is shown it; null keeps it up until it is deleted.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool IsLive(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;

    public void Update(string title, string body, AnnouncementAudience audience, DateTimeOffset? expiresAt)
    {
        Title = title;
        Body = body;
        Audience = audience;
        ExpiresAt = expiresAt;
        MarkUpdated();
    }
}
