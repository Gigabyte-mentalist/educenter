using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Crm.Domain;
using EduCenter.SharedKernel.Authorization;

namespace EduCenter.Api.Complaints;

/// <summary>Which complaints a person may open and which they may handle (docs/21.09-audit-jurnali S06.07). Permission codes and scopes only — no role names.</summary>
public static class ComplaintQueries
{
    /// <summary>
    /// What the person may read: their own, the ones handed to them, and — if they hold <c>complaints.view</c> — those of the branches they look
    /// after (the whole organization's for an organization-wide scope). A complaint the author filed is theirs to read whatever their scope, which
    /// is what lets a parent or a student follow it without ever holding the view permission.
    /// </summary>
    /// <param name="view">The caller's resolved <c>complaints.view</c> scope; null when they hold none.</param>
    public static IQueryable<Complaint> Visible(AppDbContext db, Guid organizationId, Guid userId, ResolvedScope? view)
    {
        var query = db.Complaints.Where(c => c.OrganizationId == organizationId);

        switch (view?.Scope)
        {
            case ScopeType.AllOrganization:
                return query;

            case ScopeType.MyBranches:
                var branches = view.BranchIds.ToList();
                return query.Where(c => c.AuthorUserId == userId || c.AssignedToUserId == userId || branches.Contains(c.BranchId));

            default:
                return query.Where(c => c.AuthorUserId == userId || c.AssignedToUserId == userId);
        }
    }

    /// <summary>Whether a branch is among those the scope reaches: everything for an organization-wide scope, the person's own branches for a branch one.</summary>
    public static bool Covers(ResolvedScope? scope, Guid branchId) => scope?.Scope switch
    {
        ScopeType.AllOrganization => true,
        ScopeType.MyBranches => scope.BranchIds.Contains(branchId),
        _ => false,
    };
}
