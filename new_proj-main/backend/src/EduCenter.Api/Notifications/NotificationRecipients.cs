using EduCenter.Infrastructure;
using EduCenter.Modules.Academic.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Notifications;

/// <summary>Who a message about a student, a group or a branch goes to — resolved to login user ids.</summary>
public class NotificationRecipients(AppDbContext db)
{
    public Task<List<Guid>> StudentUserAsync(Guid studentId) =>
        db.Students.Where(s => s.Id == studentId).Select(s => s.UserId).ToListAsync();

    public Task<List<Guid>> ParentsAsync(Guid studentId) =>
        (from sp in db.StudentParents
         join p in db.Parents on sp.ParentId equals p.Id
         where sp.StudentId == studentId
         select p.UserId).ToListAsync();

    public async Task<List<Guid>> StudentAndParentsAsync(Guid studentId) =>
        [.. await StudentUserAsync(studentId), .. await ParentsAsync(studentId)];

    public Task<List<Guid>> GroupStudentsAsync(Guid groupId) =>
        (from e in db.Enrollments
         join s in db.Students on e.StudentId equals s.Id
         where e.GroupId == groupId && e.Status == EnrollmentStatus.Active
         select s.UserId).Distinct().ToListAsync();

    /// <summary>The students actively enrolled in a group and the parents of those students — everyone a change to the group's
    /// schedule concerns besides its teachers. Two queries, however large the group.</summary>
    public async Task<List<Guid>> GroupStudentsAndParentsAsync(Guid groupId)
    {
        var parents = await (
            from e in db.Enrollments
            join sp in db.StudentParents on e.StudentId equals sp.StudentId
            join p in db.Parents on sp.ParentId equals p.Id
            where e.GroupId == groupId && e.Status == EnrollmentStatus.Active
            select p.UserId).Distinct().ToListAsync();
        return [.. await GroupStudentsAsync(groupId), .. parents];
    }

    public Task<List<Guid>> GroupTeachersAsync(Guid groupId, GroupStaffRole role) =>
        db.GroupStaff.Where(gs => gs.GroupId == groupId && gs.Role == role).Select(gs => gs.UserId).ToListAsync();

    public Task<List<Guid>> BranchAdministratorsAsync(Guid organizationId, Guid branchId) =>
        (from u in db.Users
         join ub in db.UserBranches on u.Id equals ub.UserId
         join ur in db.UserRoles on u.Id equals ur.UserId
         join r in db.Roles on ur.RoleId equals r.Id
         where u.OrganizationId == organizationId && ub.BranchId == branchId
             && r.Code == RoleCode.Administrator && u.Status == UserStatus.Active
         select u.Id).Distinct().ToListAsync();

    public async Task<string> StudentNameAsync(Guid studentId) =>
        await (from s in db.Students
               join u in db.Users on s.UserId equals u.Id
               where s.Id == studentId
               select u.FullName).FirstOrDefaultAsync() ?? "Student";

    public async Task<string> UserNameAsync(Guid userId) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync() ?? "Xodim";
}
