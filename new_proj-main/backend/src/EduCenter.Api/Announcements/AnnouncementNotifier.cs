using EduCenter.Api.Notifications;
using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.Modules.Students.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Announcements;

/// <summary>Telling everyone an announcement is for takes as long as the audience is large, so it runs away from the request.</summary>
public interface IAnnouncementJobQueue
{
    Task EnqueueNotificationAsync(Guid announcementId);
}

/// <summary>Production/development: a Hangfire background job.</summary>
public class HangfireAnnouncementJobQueue(IBackgroundJobClient jobs) : IAnnouncementJobQueue
{
    public Task EnqueueNotificationAsync(Guid announcementId)
    {
        jobs.Enqueue<AnnouncementNotificationJob>(job => job.RunAsync(announcementId, CancellationToken.None));
        return Task.CompletedTask;
    }
}

/// <summary>When Hangfire is off (Automation__Enabled=false, the test host) everyone is notified before the request returns.</summary>
public class InlineAnnouncementJobQueue(IServiceScopeFactory scopes) : IAnnouncementJobQueue
{
    public async Task EnqueueNotificationAsync(Guid announcementId)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AnnouncementNotifier>().NotifyAsync(announcementId);
    }
}

public class AnnouncementNotificationJob(AnnouncementNotifier notifier)
{
    /// <summary>A retry is safe: every person is notified at most once per announcement (the dedupe key), so a run that got halfway just finishes.</summary>
    [AutomaticRetry(Attempts = 2)]
    public Task RunAsync(Guid announcementId, CancellationToken ct) => notifier.NotifyAsync(announcementId, ct);
}

/// <summary>
/// Puts a published announcement into the inbox of everyone it is for — its audience, within where it applies (the organization, a branch, a group) — in
/// batches, saving after each so that a large audience is never one huge transaction. Whoever wrote it is not told about their own announcement.
/// </summary>
public class AnnouncementNotifier(AppDbContext db, NotificationDispatcher notifications)
{
    public const int BatchSize = 500;

    private static readonly string[] StaffRoles =
        [RoleCode.SuperAdmin, RoleCode.Administrator, RoleCode.Operator, RoleCode.Teacher, RoleCode.SupportTeacher];

    public async Task NotifyAsync(Guid announcementId, CancellationToken ct = default)
    {
        var announcement = await db.Announcements.AsNoTracking().FirstOrDefaultAsync(a => a.Id == announcementId, ct);
        if (announcement is null || !announcement.IsLive(DateTimeOffset.UtcNow)) return;   // deleted or expired before the job ran

        var recipients = (await RecipientsAsync(announcement, ct)).Where(id => id != announcement.AuthorId).Distinct().ToList();
        var message = new NotificationMessage(NotificationType.Announcement, announcement.Title, announcement.Body, "/announcements",
            $"announcement:{announcement.Id}");

        foreach (var batch in recipients.Chunk(BatchSize))
        {
            await notifications.NotifyAsync(announcement.OrganizationId, batch, message, ct);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private async Task<List<Guid>> RecipientsAsync(Announcement a, CancellationToken ct)
    {
        var everyone = new List<Guid>();
        var forStudents = a.Audience is AnnouncementAudience.All or AnnouncementAudience.Students;
        var forParents = a.Audience is AnnouncementAudience.All or AnnouncementAudience.Parents;
        var forStaff = a.Audience is AnnouncementAudience.All or AnnouncementAudience.Staff;

        if (forStudents || forParents)
        {
            // The students the announcement is about: a group's, a branch's (whoever is in one of its groups), or all of them.
            IQueryable<Guid> studentIds = a.GroupId is { } groupId
                ? db.Enrollments.Where(e => e.GroupId == groupId && e.Status == EnrollmentStatus.Active).Select(e => e.StudentId)
                : a.BranchId is { } branchId
                    ? from e in db.Enrollments
                      join g in db.Groups on e.GroupId equals g.Id
                      where g.BranchId == branchId && e.Status == EnrollmentStatus.Active
                      select e.StudentId
                    : db.Students.Where(s => s.OrganizationId == a.OrganizationId).Select(s => s.Id);

            if (forStudents)
                everyone.AddRange(await db.Students.Where(s => studentIds.Contains(s.Id)).Select(s => s.UserId).Distinct().ToListAsync(ct));
            if (forParents)
                everyone.AddRange(await (
                    from sp in db.StudentParents
                    join p in db.Parents on sp.ParentId equals p.Id
                    where studentIds.Contains(sp.StudentId)
                    select p.UserId).Distinct().ToListAsync(ct));
        }

        if (forStaff)
        {
            if (a.GroupId is { } staffGroup)
            {
                everyone.AddRange(await db.GroupStaff.Where(gs => gs.GroupId == staffGroup).Select(gs => gs.UserId).Distinct().ToListAsync(ct));
            }
            else
            {
                var staff =
                    from u in db.Users
                    join ur in db.UserRoles on u.Id equals ur.UserId
                    join r in db.Roles on ur.RoleId equals r.Id
                    where u.OrganizationId == a.OrganizationId && u.Status == UserStatus.Active && StaffRoles.Contains(r.Code)
                    select u;
                if (a.BranchId is { } staffBranch)
                    staff = staff.Where(u => db.UserBranches.Any(ub => ub.UserId == u.Id && ub.BranchId == staffBranch));
                everyone.AddRange(await staff.Select(u => u.Id).Distinct().ToListAsync(ct));
            }
        }

        return everyone;
    }
}
