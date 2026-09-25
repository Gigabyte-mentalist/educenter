using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Users;

/// <summary>Who may act on another person's account — shared by the user endpoints and the profile-picture endpoints, so the two
/// can never disagree about whom a branch Administrator reaches.</summary>
public static class UserAccess
{
    /// <summary>Same reasoning as the roles a branch Administrator may hand out, for touching an existing account rather than
    /// creating one: a branch Administrator editing, blocking or deleting a colleague must not reach someone org-wide (S01.04 —
    /// this was open: a Chilonzor Administrator could block the Super Admin).</summary>
    public static readonly HashSet<string> ProtectedFromBranchScope =
    [
        RoleCode.SuperAdmin, RoleCode.Administrator, RoleCode.Developer,
    ];

    /// <summary>A branch-scoped caller may only touch users who share at least one of its branches (or
    /// themselves), and — regardless of a shared branch — never someone holding a <see cref="ProtectedFromBranchScope"/>
    /// role: a branch Administrator has no business editing, blocking or deleting a Super Admin, an Administrator of
    /// another branch, or a Developer, even one assigned to their own branch. Org-wide scope may touch anyone; every other scope
    /// (a teacher, a student, a parent) only themselves. The caller has already limited <paramref name="user"/> to the caller's
    /// organization. Requires <c>user.Roles</c> and <c>user.Branches</c> to already be loaded.</summary>
    public static async Task<bool> CanTouchAsync(this ResolvedScope scope, AppDbContext db, User user, CancellationToken ct = default)
    {
        if (scope.Scope == ScopeType.AllOrganization) return true;
        if (scope.Scope != ScopeType.MyBranches) return user.Id == scope.UserId;
        if (user.Id == scope.UserId) return true;
        if (!user.Branches.Any(b => scope.BranchIds.Contains(b.BranchId))) return false;

        var roleIds = user.Roles.Select(ur => ur.RoleId).ToList();
        var hasProtectedRole = await db.Roles.AnyAsync(r => roleIds.Contains(r.Id) && ProtectedFromBranchScope.Contains(r.Code), ct);
        return !hasProtectedRole;
    }
}
