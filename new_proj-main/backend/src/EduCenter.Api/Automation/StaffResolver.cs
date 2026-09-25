using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using EduCenter.Modules.Students.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Automation;

/// <summary>Who picks up a task the system opens about a student (absence, debt): the operator who owned
/// the lead the student was converted from; otherwise the first active operator of the student's branch;
/// otherwise that branch's administrator.</summary>
public class StaffResolver(AppDbContext db)
{
    public async Task<Guid?> ResolveOperatorForStudentAsync(Guid organizationId, Guid studentId, Guid? branchId, CancellationToken ct = default)
    {
        var leadOperator = await db.Leads
            .Where(l => l.ConvertedStudentId == studentId)
            .Select(l => (Guid?)l.OperatorId)
            .FirstOrDefaultAsync(ct);
        if (leadOperator is not null) return leadOperator;

        branchId ??= await (
            from e in db.Enrollments
            join g in db.Groups on e.GroupId equals g.Id
            where e.StudentId == studentId && e.Status == EnrollmentStatus.Active
            orderby e.EnrolledAt descending
            select (Guid?)g.BranchId
        ).FirstOrDefaultAsync(ct);
        if (branchId is null) return null;

        foreach (var roleCode in new[] { RoleCode.Operator, RoleCode.Administrator })
        {
            var userId = await (
                from u in db.Users
                join ub in db.UserBranches on u.Id equals ub.UserId
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                where u.OrganizationId == organizationId && ub.BranchId == branchId.Value
                    && r.Code == roleCode && u.Status == UserStatus.Active
                orderby u.CreatedAt
                select (Guid?)u.Id
            ).FirstOrDefaultAsync(ct);
            if (userId is not null) return userId;
        }

        return null;
    }
}
