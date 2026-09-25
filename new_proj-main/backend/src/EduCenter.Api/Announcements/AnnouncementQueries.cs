using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Announcements;

/// <summary>Which announcements a person may read and which they may change (docs/21.09-audit-jurnali S06.05). No role names anywhere: the audience a
/// person belongs to comes from the <em>kind of scope</em> their <c>announcements.view</c> grant has.</summary>
public static class AnnouncementQueries
{
    /// <summary>A student reads what is meant for students, a parent what is meant for parents, everyone else what is meant for staff.
    /// (Self → a student's own scope; MyChildren → a parent's.)</summary>
    public static AnnouncementAudience AudienceOf(ScopeType scope) => scope switch
    {
        ScopeType.Self => AnnouncementAudience.Students,
        ScopeType.MyChildren => AnnouncementAudience.Parents,
        _ => AnnouncementAudience.Staff,
    };

    /// <summary>
    /// The live announcements that reach this person: for the whole organization, for a branch they belong to, or for a group they are in
    /// (a student's or teacher's own, a parent's children's). Addressed to their audience or to everybody — unless <paramref name="seeEveryAudience"/>,
    /// which is for whoever manages announcements and has to see what they addressed to others. An expired one is gone for everyone.
    /// </summary>
    public static async Task<IQueryable<Announcement>> VisibleAsync(
        AppDbContext db, ResolvedScope scope, bool seeEveryAudience, DateTimeOffset now)
    {
        var query = db.Announcements.Where(a => a.OrganizationId == scope.OrganizationId && (a.ExpiresAt == null || a.ExpiresAt > now));

        if (!seeEveryAudience)
        {
            var mine = AudienceOf(scope.Scope);
            query = query.Where(a => a.Audience == AnnouncementAudience.All || a.Audience == mine);
        }

        switch (scope.Scope)
        {
            case ScopeType.AllOrganization:
                return query;

            case ScopeType.MyBranches:
                // A group's announcement carries its branch, so branch membership covers it too.
                var own = scope.BranchIds.ToList();
                return query.Where(a => a.BranchId == null || own.Contains(a.BranchId.Value));

            default:
                var groups = await scope.VisibleGroupIdsAsync(db) ?? [];
                var branches = scope.BranchIds.ToList();
                branches.AddRange(await db.Groups.Where(g => groups.Contains(g.Id)).Select(g => g.BranchId).Distinct().ToListAsync());
                return query.Where(a =>
                    (a.GroupId == null && (a.BranchId == null || branches.Contains(a.BranchId.Value)))
                    || (a.GroupId != null && groups.Contains(a.GroupId.Value)));
        }
    }

    /// <summary>The whole organization's announcements are the organization-wide manager's; a branch's and its groups' are also the branch's own managers'.</summary>
    public static bool CanManage(ResolvedScope? manage, Announcement announcement) => manage?.Scope switch
    {
        ScopeType.AllOrganization => true,
        ScopeType.MyBranches => announcement.BranchId is { } branchId && manage.BranchIds.Contains(branchId),
        _ => false,
    };
}
