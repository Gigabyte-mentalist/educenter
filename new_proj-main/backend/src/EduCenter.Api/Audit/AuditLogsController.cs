using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Audit;

public record AuditLogDto(
    Guid Id, Guid UserId, string? UserName, string Action, string Resource, Guid ResourceId,
    string? OldValueJson, string? NewValueJson, string? Reason, DateTimeOffset CreatedAt);

[ApiController]
[Route("api/audit-logs")]
public class AuditLogsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "audit.log.view")]
    public async Task<ActionResult<Page<AuditLogDto>>> List(
        [FromQuery] string? resource, [FromQuery] Guid? resourceId, [FromQuery] string? action,
        [FromQuery] Guid? userId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope is not (ScopeType.AllOrganization or ScopeType.MyBranches))
            return Forbid();

        var page = paging ?? new PageRequest();
        var query = db.AuditLogs.Where(a => a.OrganizationId == scope.OrganizationId);

        if (resource is not null) query = query.Where(a => a.Resource == resource);
        if (resourceId is not null) query = query.Where(a => a.ResourceId == resourceId);
        if (action is not null) query = query.Where(a => a.Action == action);
        if (userId is not null) query = query.Where(a => a.UserId == userId);
        if (from is not null) query = query.Where(a => a.CreatedAt >= from);
        if (to is not null) query = query.Where(a => a.CreatedAt <= to);

        var total = await query.CountAsync(ct);

        // The name is joined in rather than resolved client-side from the user list: the audit trail outlives the
        // account, so a log written by someone since deleted must still say who it was — and the page the client
        // holds no longer contains every user anyway.
        var logs = await (
            from a in query
            join u in db.Users on a.UserId equals u.Id into actor
            from u in actor.DefaultIfEmpty()
            orderby a.CreatedAt descending, a.Id
            select new AuditLogDto(
                a.Id, a.UserId, u == null ? null : u.FullName, a.Action, a.Resource, a.ResourceId,
                a.OldValueJson, a.NewValueJson, a.Reason, a.CreatedAt))
            .Skip(page.Skip).Take(page.Take)
            .ToListAsync(ct);

        return Ok(new Page<AuditLogDto>(logs, total, page.Number, page.Take));
    }

    /// <summary>The distinct action/resource values present, so the filter dropdowns offer only what exists.</summary>
    [HttpGet("filters")]
    [Authorize(Policy = "audit.log.view")]
    public async Task<ActionResult<object>> Filters(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope is not (ScopeType.AllOrganization or ScopeType.MyBranches))
            return Forbid();

        var query = db.AuditLogs.Where(a => a.OrganizationId == scope.OrganizationId);
        return Ok(new
        {
            actions = await query.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct),
            resources = await query.Select(a => a.Resource).Distinct().OrderBy(r => r).ToListAsync(ct),
        });
    }
}
