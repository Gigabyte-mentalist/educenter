using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Authorization;

/// <summary>Resolves whether the caller's scope covers a given group/branch — shared by Attendance and
/// Homework controllers, both of which gate on "is this user staff on this group" the same way.</summary>
public static class GroupAccess
{
    public static async Task<bool> CanAccessGroupAsync(
        this ResolvedScope scope, AppDbContext db, Guid groupId, Guid branchId)
    {
        return scope.Scope switch
        {
            ScopeType.AllOrganization => true,
            ScopeType.MyBranches => scope.BranchIds.Contains(branchId),
            ScopeType.MyGroups => await db.GroupStaff.AnyAsync(gs =>
                gs.GroupId == groupId && gs.UserId == scope.UserId && gs.Role == GroupStaffRole.MainTeacher),
            ScopeType.AssignedGroups => await db.GroupStaff.AnyAsync(gs =>
                gs.GroupId == groupId && gs.UserId == scope.UserId && gs.Role == GroupStaffRole.SupportTeacher),
            ScopeType.Self => await (
                from e in db.Enrollments
                join s in db.Students on e.StudentId equals s.Id
                where e.GroupId == groupId && s.UserId == scope.UserId && e.Status == EnrollmentStatus.Active
                select e.Id).AnyAsync(),
            ScopeType.MyChildren => await (
                from e in db.Enrollments
                join sp in db.StudentParents on e.StudentId equals sp.StudentId
                join p in db.Parents on sp.ParentId equals p.Id
                where e.GroupId == groupId && p.UserId == scope.UserId && e.Status == EnrollmentStatus.Active
                select e.Id).AnyAsync(),
            _ => false
        };
    }

    /// <summary>
    /// The set of group ids the caller may see for list endpoints. Returns <c>null</c> for
    /// AllOrganization/MyBranches (meaning: filter by branch instead, no group restriction);
    /// otherwise the explicit group ids from staffing (teachers) or enrollment (students/parents).
    /// </summary>
    public static async Task<List<Guid>?> VisibleGroupIdsAsync(this ResolvedScope scope, AppDbContext db)
    {
        switch (scope.Scope)
        {
            case ScopeType.AllOrganization:
            case ScopeType.MyBranches:
                return null;

            case ScopeType.MyGroups:
                return await db.GroupStaff
                    .Where(gs => gs.UserId == scope.UserId && gs.Role == GroupStaffRole.MainTeacher)
                    .Select(gs => gs.GroupId).Distinct().ToListAsync();

            case ScopeType.AssignedGroups:
                return await db.GroupStaff
                    .Where(gs => gs.UserId == scope.UserId && gs.Role == GroupStaffRole.SupportTeacher)
                    .Select(gs => gs.GroupId).Distinct().ToListAsync();

            case ScopeType.Self:
                return await (
                    from e in db.Enrollments
                    join s in db.Students on e.StudentId equals s.Id
                    where s.UserId == scope.UserId && e.Status == EnrollmentStatus.Active
                    select e.GroupId).Distinct().ToListAsync();

            case ScopeType.MyChildren:
                return await (
                    from e in db.Enrollments
                    join sp in db.StudentParents on e.StudentId equals sp.StudentId
                    join p in db.Parents on sp.ParentId equals p.Id
                    where p.UserId == scope.UserId && e.Status == EnrollmentStatus.Active
                    select e.GroupId).Distinct().ToListAsync();

            default:
                return [];
        }
    }

    /// <summary>Applies the caller's scope to a group query: branch-restricted for org/branch scopes,
    /// explicit group ids for everyone else.</summary>
    public static async Task<IQueryable<Group>> ScopedGroupsAsync(this ResolvedScope scope, AppDbContext db)
    {
        var query = db.Groups.Where(g => g.OrganizationId == scope.OrganizationId);

        if (scope.Scope == ScopeType.MyBranches)
            return query.Where(g => scope.BranchIds.Contains(g.BranchId));

        var groupIds = await scope.VisibleGroupIdsAsync(db);
        return groupIds is null ? query : query.Where(g => groupIds.Contains(g.Id));
    }

    /// <summary>True for scopes that see one student's own records (Self) or a parent's children's
    /// (MyChildren) — resolved separately since it depends on Student/Parent, not group staffing.</summary>
    public static bool IsPersonalScope(this ResolvedScope scope) =>
        scope.Scope is ScopeType.Self or ScopeType.MyChildren;

    /// <summary>Self → the caller's own Student.Id; MyChildren → the Student.Ids the caller (a Parent)
    /// guards; anything else → empty (callers with broad scope don't use this).</summary>
    public static async Task<List<Guid>> VisibleStudentIdsAsync(this ResolvedScope scope, AppDbContext db)
    {
        if (scope.Scope == ScopeType.Self)
        {
            var student = await db.Students.FirstOrDefaultAsync(s => s.UserId == scope.UserId);
            return student is null ? [] : [student.Id];
        }

        if (scope.Scope == ScopeType.MyChildren)
        {
            var parent = await db.Parents.FirstOrDefaultAsync(p => p.UserId == scope.UserId);
            if (parent is null) return [];
            return await db.StudentParents.Where(sp => sp.ParentId == parent.Id).Select(sp => sp.StudentId).ToListAsync();
        }

        return [];
    }
}
