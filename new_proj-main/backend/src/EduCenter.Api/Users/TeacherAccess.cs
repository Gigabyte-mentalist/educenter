using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Users;

public enum TeacherAccessResult
{
    Allowed,
    NotFound,
    Forbidden
}

/// <summary>
/// Who may open a teacher's page, and everything that hangs off it (workload, portfolio, ...). One rule for all of them:
/// the teacher themselves, or a manager with a broad scope — and for a branch administrator only when their branches overlap
/// the teacher's. A colleague sees the teacher in the list but not the page.
/// </summary>
public static class TeacherAccess
{
    /// <summary>Users who teach: the two teaching roles. There is no separate Teacher entity — a teacher is a User.</summary>
    public static IQueryable<User> TeacherUsers(this AppDbContext db) => db.Users.Where(u => u.Roles.Any(ur =>
        db.Roles.Any(r => r.Id == ur.RoleId && (r.Code == RoleCode.Teacher || r.Code == RoleCode.SupportTeacher))));

    public static bool HasBroadAccess(this ResolvedScope scope) =>
        scope.Scope is ScopeType.AllOrganization or ScopeType.MyBranches;

    public static async Task<TeacherAccessResult> CheckTeacherAccessAsync(
        this AppDbContext db, ResolvedScope scope, Guid teacherId, CancellationToken ct = default)
    {
        if (!await db.TeacherUsers().AnyAsync(u => u.Id == teacherId && u.OrganizationId == scope.OrganizationId, ct))
            return TeacherAccessResult.NotFound;

        if (scope.Scope == ScopeType.MyBranches && !await db.UserBranches.AnyAsync(b =>
                b.UserId == teacherId && scope.BranchIds.Contains(b.BranchId), ct))
            return TeacherAccessResult.Forbidden;

        // Any holder of users.view may open their own teacher page — that is how a Teacher (whose users.view grant is
        // scoped to MyGroups, not Self) reaches these endpoints at all.
        if (scope.UserId != teacherId && !scope.HasBroadAccess()) return TeacherAccessResult.Forbidden;

        return TeacherAccessResult.Allowed;
    }
}
