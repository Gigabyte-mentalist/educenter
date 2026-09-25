using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Observability;

public record DataAccessRequestDto(
    Guid Id, Guid RequesterId, string RequesterName, string PermissionCode, string Reason, int Hours, DataAccessStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? DecidedByName, string? DecisionNote, DateTimeOffset? ExpiresAt,
    bool IsActive);

public record CreateDataAccessRequest(string PermissionCode, string Reason, int Hours);
public record DecideDataAccessRequest(int? Hours, string? Note);

/// <summary>
/// Temporary access to production data (docs/00_TZ.md §3.8): Request → Reason → Approval → Expiration → Audit Log. A Developer
/// asks for one read permission, an organization-wide approver decides, and <see cref="PermissionResolver"/> honours the grant
/// until it expires. The requests themselves are in the audit log's scope.
/// </summary>
[ApiController]
[Route("api/system/data-access")]
public class DataAccessController(AppDbContext db) : ControllerBase
{
    /// <summary>The caller's own requests, newest first — the Developer's view.</summary>
    [HttpGet("mine")]
    [Authorize(Policy = "production.data.request")]
    public async Task<ActionResult<List<DataAccessRequestDto>>> Mine(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var rows = await db.DataAccessRequests.AsNoTracking()
            .Where(r => r.OrganizationId == scope.OrganizationId && r.RequesterId == scope.UserId)
            .OrderByDescending(r => r.CreatedAt).Take(100).ToListAsync(ct);
        return Ok(await ToDtosAsync(rows, ct));
    }

    /// <summary>Every request of the organization — the approver's queue, pending first.</summary>
    [HttpGet]
    [Authorize(Policy = "production.data.approve")]
    public async Task<ActionResult<List<DataAccessRequestDto>>> List(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var rows = await db.DataAccessRequests.AsNoTracking()
            .Where(r => r.OrganizationId == scope.OrganizationId)
            .OrderBy(r => r.Status == DataAccessStatus.Pending ? 0 : 1).ThenByDescending(r => r.CreatedAt)
            .Take(200).ToListAsync(ct);
        return Ok(await ToDtosAsync(rows, ct));
    }

    [HttpPost]
    [Authorize(Policy = "production.data.request")]
    public async Task<ActionResult<DataAccessRequestDto>> Create(CreateDataAccessRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();

        if (!DataAccessRequest.GrantableCodes.Contains(request.PermissionCode))
            return BadRequest(new { error = "Bu ruxsatni so'rab bo'lmaydi. Faqat o'quv ma'lumotlarini ko'rish so'raladi." });
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length < DataAccessRequest.MinReasonLength)
            return BadRequest(new { error = $"Sababni kamida {DataAccessRequest.MinReasonLength} belgi bilan yozing." });
        if (reason.Length > DataAccessRequest.MaxReasonLength)
            return BadRequest(new { error = $"Sabab {DataAccessRequest.MaxReasonLength} belgidan oshmasin." });
        if (request.Hours is < 1 or > DataAccessRequest.MaxHours)
            return BadRequest(new { error = $"Muddat 1 dan {DataAccessRequest.MaxHours} soatgacha bo'lishi kerak." });

        var now = DateTimeOffset.UtcNow;
        var open = await db.DataAccessRequests.AnyAsync(r =>
            r.RequesterId == scope.UserId && r.PermissionCode == request.PermissionCode
            && (r.Status == DataAccessStatus.Pending || (r.Status == DataAccessStatus.Approved && r.ExpiresAt > now)), ct);
        if (open) return Conflict(new { error = "Bu ruxsat uchun so'rov allaqachon kutilmoqda yoki amal qilmoqda." });

        var entity = new DataAccessRequest(scope.OrganizationId, scope.UserId, request.PermissionCode, reason, request.Hours);
        db.DataAccessRequests.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok((await ToDtosAsync([entity], ct)).Single());
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "production.data.approve")]
    public Task<ActionResult<DataAccessRequestDto>> Approve(Guid id, DecideDataAccessRequest body, CancellationToken ct) =>
        DecideAsync(id, ct, (entity, by, now) => entity.Approve(by, body.Hours ?? entity.Hours, body.Note, now));

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "production.data.approve")]
    public Task<ActionResult<DataAccessRequestDto>> Reject(Guid id, DecideDataAccessRequest body, CancellationToken ct) =>
        DecideAsync(id, ct, (entity, by, now) => entity.Reject(by, body.Note, now));

    [HttpPost("{id:guid}/revoke")]
    [Authorize(Policy = "production.data.approve")]
    public Task<ActionResult<DataAccessRequestDto>> Revoke(Guid id, DecideDataAccessRequest body, CancellationToken ct) =>
        DecideAsync(id, ct, (entity, by, now) => entity.Revoke(by, body.Note, now));

    private async Task<ActionResult<DataAccessRequestDto>> DecideAsync(
        Guid id, CancellationToken ct, Action<DataAccessRequest, Guid, DateTimeOffset> decide)
    {
        var scope = HttpContext.RequireResolvedScope();
        var entity = await db.DataAccessRequests.FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == scope.OrganizationId, ct);
        if (entity is null) return NotFound();

        try { decide(entity, scope.UserId, DateTimeOffset.UtcNow); }
        catch (InvalidOperationException e) { return Conflict(new { error = e.Message }); }

        await db.SaveChangesAsync(ct);
        return Ok((await ToDtosAsync([entity], ct)).Single());
    }

    private async Task<List<DataAccessRequestDto>> ToDtosAsync(IReadOnlyList<DataAccessRequest> rows, CancellationToken ct)
    {
        var userIds = rows.Select(r => r.RequesterId).Concat(rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value)).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var now = DateTimeOffset.UtcNow;

        return rows.Select(r => new DataAccessRequestDto(
            r.Id, r.RequesterId, names.GetValueOrDefault(r.RequesterId, "—"), r.PermissionCode, r.Reason, r.Hours, r.Status,
            r.CreatedAt, r.DecidedAt, r.DecidedBy is { } by ? names.GetValueOrDefault(by) : null, r.DecisionNote, r.ExpiresAt,
            r.IsActive(now))).ToList();
    }
}
