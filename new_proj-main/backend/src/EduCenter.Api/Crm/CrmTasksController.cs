using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Crm.Domain;
using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Crm;

/// <summary>A queue row carries who it is about (<see cref="SubjectName"/> / <see cref="SubjectPhone"/>,
/// from the lead or the student) and why it exists (<see cref="ReasonCode"/> / <see cref="Note"/>), so the
/// Call Queue can render student tasks without a second lookup the operator's scope may not allow.</summary>
public record CrmTaskDto(
    Guid Id, Guid? LeadId, Guid? StudentId, Guid AssignedTo,
    CrmTaskType Type, CrmTaskStatus Status, DateTimeOffset DueAt,
    string? ReasonCode, string? Note, string? SubjectName, string? SubjectPhone);

/// <summary>Backs the Operator's "Today's Tasks / Call Queue" (docs/00_TZ.md §3.3).</summary>
[ApiController]
[Route("api/crm/tasks")]
public class CrmTasksController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "crm.task.view")]
    public async Task<ActionResult<Page<CrmTaskDto>>> List(
        [FromQuery] bool includeCompleted = false, [FromQuery] string? bucket = null, [FromQuery] PageRequest? paging = null,
        CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var query = ScopedTasks(scope);

        if (!includeCompleted)
            query = query.Where(t => t.Status == CrmTaskStatus.Open);

        // The queue screen shows what is late apart from what is still to come, each with its own count and pager.
        var now = DateTimeOffset.UtcNow;
        query = bucket?.ToLowerInvariant() switch
        {
            "overdue" => query.Where(t => t.DueAt < now),
            "upcoming" => query.Where(t => t.DueAt >= now),
            _ => query
        };

        return Ok(await query
            .OrderBy(t => t.DueAt).ThenBy(t => t.Id)
            .Select(t => new CrmTaskDto(
                t.Id, t.LeadId, t.StudentId, t.AssignedTo, t.Type, t.Status, t.DueAt, t.ReasonCode, t.Note,
                t.LeadId != null
                    ? db.Leads.Where(l => l.Id == t.LeadId).Select(l => l.FullName).FirstOrDefault()
                    : db.Students.Where(s => s.Id == t.StudentId)
                        .Join(db.Users, s => s.UserId, u => u.Id, (s, u) => u.FullName).FirstOrDefault(),
                t.LeadId != null
                    ? db.Leads.Where(l => l.Id == t.LeadId).Select(l => l.Phone).FirstOrDefault()
                    : db.Students.Where(s => s.Id == t.StudentId)
                        .Join(db.Users, s => s.UserId, u => u.Id, (s, u) => u.Phone).FirstOrDefault()))
            .ToPageAsync(paging, ct));
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = "crm.task.view")]
    public async Task<IActionResult> Complete(Guid id)
    {
        var scope = HttpContext.RequireResolvedScope();

        // Same visibility as the list: an operator closes only their own queue items, a branch
        // administrator only tasks about leads/students of their branches.
        var task = await ScopedTasks(scope).FirstOrDefaultAsync(t => t.Id == id);
        if (task is null)
        {
            var exists = await db.CrmTasks.AnyAsync(t => t.Id == id && t.OrganizationId == scope.OrganizationId);
            return exists ? Forbid() : NotFound();
        }

        task.Complete();
        await db.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<CrmTask> ScopedTasks(ResolvedScope scope)
    {
        var query = db.CrmTasks.Where(t => t.OrganizationId == scope.OrganizationId);

        return scope.Scope switch
        {
            ScopeType.AllOrganization => query,
            // Lead tasks belong to the lead's branch; student tasks (absence, debt) to the branch of any
            // group the student is enrolled in. Previously only lead tasks were matched, so a branch
            // administrator never saw student-level tasks at all.
            ScopeType.MyBranches => query.Where(t =>
                (t.LeadId != null && db.Leads.Any(l => l.Id == t.LeadId && scope.BranchIds.Contains(l.BranchId)))
                || (t.StudentId != null && db.Enrollments.Any(e => e.StudentId == t.StudentId
                    && db.Groups.Any(g => g.Id == e.GroupId && scope.BranchIds.Contains(g.BranchId))))),
            ScopeType.Self => query.Where(t => t.AssignedTo == scope.UserId),
            _ => query.Where(_ => false)
        };
    }
}
