using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Students.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Authorization;

/// <summary>Resolves whether the caller's scope covers a given student — used by Student 360° endpoints
/// (overview/timeline), which unlike Attendance/Homework aren't keyed off a single lesson/group.</summary>
public static class StudentAccess
{
    public static async Task<bool> CanAccessStudentAsync(this ResolvedScope scope, AppDbContext db, Guid studentId)
    {
        switch (scope.Scope)
        {
            case ScopeType.AllOrganization:
                return true;

            case ScopeType.MyBranches:
                return await (
                    from e in db.Enrollments
                    join g in db.Groups on e.GroupId equals g.Id
                    where e.StudentId == studentId && scope.BranchIds.Contains(g.BranchId)
                    select e.Id
                ).AnyAsync();

            case ScopeType.MyGroups:
                return await (
                    from e in db.Enrollments
                    join gs in db.GroupStaff on e.GroupId equals gs.GroupId
                    where e.StudentId == studentId && gs.UserId == scope.UserId && gs.Role == GroupStaffRole.MainTeacher
                    select e.Id
                ).AnyAsync();

            case ScopeType.AssignedGroups:
                return await (
                    from e in db.Enrollments
                    join gs in db.GroupStaff on e.GroupId equals gs.GroupId
                    where e.StudentId == studentId && gs.UserId == scope.UserId && gs.Role == GroupStaffRole.SupportTeacher
                    select e.Id
                ).AnyAsync();

            case ScopeType.Self:
                return await db.Students.AnyAsync(s => s.Id == studentId && s.UserId == scope.UserId);

            case ScopeType.MyChildren:
                return await (
                    from sp in db.StudentParents
                    join p in db.Parents on sp.ParentId equals p.Id
                    where sp.StudentId == studentId && p.UserId == scope.UserId
                    select sp.StudentId
                ).AnyAsync();

            default:
                return false;
        }
    }

    /// <summary>
    /// Every student the caller's scope covers, as a query to compose into a bigger one. The singular
    /// <see cref="CanAccessStudentAsync"/> answers "may I open this student"; this answers "which students are
    /// mine", which is what a list screen needs — asking the singular question per row is a query per row.
    /// </summary>
    public static IQueryable<Guid>? ScopedStudentIds(this ResolvedScope scope, AppDbContext db) =>
        scope.Scope switch
        {
            ScopeType.AllOrganization => db.Students
                .Where(s => s.OrganizationId == scope.OrganizationId).Select(s => s.Id),

            ScopeType.MyBranches =>
                from e in db.Enrollments
                join g in db.Groups on e.GroupId equals g.Id
                where scope.BranchIds.Contains(g.BranchId)
                select e.StudentId,

            ScopeType.MyGroups =>
                from e in db.Enrollments
                join gs in db.GroupStaff on e.GroupId equals gs.GroupId
                where gs.UserId == scope.UserId && gs.Role == GroupStaffRole.MainTeacher
                select e.StudentId,

            ScopeType.AssignedGroups =>
                from e in db.Enrollments
                join gs in db.GroupStaff on e.GroupId equals gs.GroupId
                where gs.UserId == scope.UserId && gs.Role == GroupStaffRole.SupportTeacher
                select e.StudentId,

            ScopeType.Self => db.Students.Where(s => s.UserId == scope.UserId).Select(s => s.Id),

            ScopeType.MyChildren =>
                from sp in db.StudentParents
                join p in db.Parents on sp.ParentId equals p.Id
                where p.UserId == scope.UserId
                select sp.StudentId,

            _ => null,
        };
}
