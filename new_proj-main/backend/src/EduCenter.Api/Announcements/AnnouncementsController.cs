using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Notifications.Domain;
using EduCenter.SharedKernel;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Announcements;

/// <param name="BranchId">Empty together with <paramref name="GroupId"/>: the whole organization.</param>
/// <param name="CanManage">Whether the caller may edit or delete this one — the list draws the buttons from it.</param>
public record AnnouncementDto(
    Guid Id, string Title, string Body, AnnouncementAudience Audience,
    Guid? BranchId, string? BranchName, Guid? GroupId, string? GroupName,
    Guid AuthorId, string AuthorName, DateTimeOffset PublishedAt, DateTimeOffset? ExpiresAt, bool CanManage);

public record CreateAnnouncementRequest(
    string Title, string Body, AnnouncementAudience Audience, Guid? BranchId, Guid? GroupId, DateTimeOffset? ExpiresAt);

/// <summary>What was sent changes; the rest stays. Where it applies cannot be changed — delete and publish again.</summary>
public record UpdateAnnouncementRequest(
    string? Title, string? Body, AnnouncementAudience? Audience, DateTimeOffset? ExpiresAt, bool ClearExpiry = false);

/// <summary>Announcements (docs/00_TZ.md §24, docs/21.09-audit-jurnali S06.05): the centre's notices to students, parents and staff.</summary>
[ApiController]
[Route("api/announcements")]
public class AnnouncementsController(
    AppDbContext db, PermissionResolver permissions, IAnnouncementJobQueue notifications) : ControllerBase
{
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 4000;

    /// <summary>The live announcements that reach the caller, newest first. Whoever may manage announcements also sees the ones addressed to other audiences.</summary>
    [HttpGet]
    [Authorize(Policy = "announcements.view")]
    public async Task<ActionResult<Page<AnnouncementDto>>> List([FromQuery] PageRequest? paging, CancellationToken ct = default)
    {
        var scope = HttpContext.RequireResolvedScope();
        var manage = await permissions.ResolveAsync(scope.UserId, "announcements.manage", ct);

        var query = await AnnouncementQueries.VisibleAsync(db, scope, seeEveryAudience: manage is not null, DateTimeOffset.UtcNow);
        var page = await query.OrderByDescending(a => a.PublishedAt).ThenBy(a => a.Id).ToPageAsync(paging, ct);

        var dtos = await ToDtosAsync(page.Items, manage, ct);
        return Ok(new Page<AnnouncementDto>(dtos, page.Total, page.PageNumber, page.PageSize));
    }

    [HttpPost]
    [Authorize(Policy = "announcements.manage")]
    public async Task<ActionResult<AnnouncementDto>> Create(CreateAnnouncementRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();

        var title = request.Title?.Trim() ?? "";
        var body = request.Body?.Trim() ?? "";
        if (Validate(title, body, request.Audience, request.ExpiresAt) is { } problem) return BadRequest(new { error = problem });

        // Where it applies. A group carries its branch; a branch is one the caller looks after; the whole organization is for whoever looks after all of it.
        var branchId = request.BranchId;
        if (request.GroupId is { } groupId)
        {
            var group = await db.Groups.Where(g => g.Id == groupId && g.OrganizationId == scope.OrganizationId)
                .Select(g => new { g.BranchId }).FirstOrDefaultAsync(ct);
            if (group is null) return NotFound(new { error = "Guruh topilmadi." });
            if (branchId is not null && branchId != group.BranchId)
                return BadRequest(new { error = "Guruh ko'rsatilgan filialga tegishli emas." });
            if (!await scope.CanAccessGroupAsync(db, groupId, group.BranchId)) return Forbid();
            branchId = group.BranchId;
        }
        else if (branchId is { } wantedBranch)
        {
            var orgBranchIds = await db.Branches.Where(b => b.OrganizationId == scope.OrganizationId).Select(b => b.Id).ToListAsync(ct);
            if (scope.AllowedBranchIds(orgBranchIds)?.Contains(wantedBranch) != true) return Forbid();
        }
        else if (scope.Scope != ScopeType.AllOrganization)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Butun tashkilotga e'lon berish uchun ruxsat yo'q — filial yoki guruhni tanlang." });
        }

        var announcement = new Announcement(scope.OrganizationId, branchId, request.GroupId, title, body, request.Audience, scope.UserId, request.ExpiresAt);
        db.Announcements.Add(announcement);
        await db.SaveChangesAsync(ct);

        await notifications.EnqueueNotificationAsync(announcement.Id);

        var manage = await permissions.ResolveAsync(scope.UserId, "announcements.manage", ct);
        return CreatedAtAction(nameof(List), (await ToDtosAsync([announcement], manage, ct))[0]);
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "announcements.manage")]
    public async Task<ActionResult<AnnouncementDto>> Update(Guid id, UpdateAnnouncementRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id && a.OrganizationId == scope.OrganizationId, ct);
        if (announcement is null) return NotFound();
        if (!AnnouncementQueries.CanManage(scope, announcement)) return Forbid();

        var title = request.Title?.Trim() ?? announcement.Title;
        var body = request.Body?.Trim() ?? announcement.Body;
        var audience = request.Audience ?? announcement.Audience;
        DateTimeOffset? expiresAt = request.ClearExpiry ? null : request.ExpiresAt ?? announcement.ExpiresAt;
        // A date that has passed is fine to keep, not to set: setting it would hide the announcement at once.
        var newExpiry = request.ClearExpiry ? null : request.ExpiresAt;
        if (Validate(title, body, audience, newExpiry) is { } problem) return BadRequest(new { error = problem });

        announcement.Update(title, body, audience, expiresAt);
        await db.SaveChangesAsync(ct);

        return Ok((await ToDtosAsync([announcement], scope, ct))[0]);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "announcements.manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var announcement = await db.Announcements.FirstOrDefaultAsync(a => a.Id == id && a.OrganizationId == scope.OrganizationId, ct);
        if (announcement is null) return NotFound();
        if (!AnnouncementQueries.CanManage(scope, announcement)) return Forbid();

        db.Announcements.Remove(announcement);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string? Validate(string title, string body, AnnouncementAudience audience, DateTimeOffset? newExpiry)
    {
        if (title.Length == 0) return "Sarlavha bo'sh bo'lmasin.";
        if (title.Length > MaxTitleLength) return $"Sarlavha {MaxTitleLength} belgidan oshmasin.";
        if (body.Length == 0) return "Matn bo'sh bo'lmasin.";
        if (body.Length > MaxBodyLength) return $"Matn {MaxBodyLength} belgidan oshmasin.";
        if (!Enum.IsDefined(audience)) return "Auditoriya noto'g'ri: All, Students, Parents yoki Staff.";
        if (newExpiry is { } expiry && expiry <= DateTimeOffset.UtcNow) return "Amal qilish muddati kelajakda bo'lishi kerak.";
        return null;
    }

    private async Task<List<AnnouncementDto>> ToDtosAsync(IReadOnlyCollection<Announcement> items, ResolvedScope? manage, CancellationToken ct)
    {
        var branchIds = items.Where(a => a.BranchId is not null).Select(a => a.BranchId!.Value).Distinct().ToList();
        var groupIds = items.Where(a => a.GroupId is not null).Select(a => a.GroupId!.Value).Distinct().ToList();
        var authorIds = items.Select(a => a.AuthorId).Distinct().ToList();

        var branches = await db.Branches.Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var groups = await db.Groups.Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        var authors = await db.Users.Where(u => authorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return items.Select(a => new AnnouncementDto(
            a.Id, a.Title, a.Body, a.Audience,
            a.BranchId, a.BranchId is { } b ? branches.GetValueOrDefault(b) : null,
            a.GroupId, a.GroupId is { } g ? groups.GetValueOrDefault(g) : null,
            a.AuthorId, authors.GetValueOrDefault(a.AuthorId, "Xodim"), a.PublishedAt, a.ExpiresAt,
            AnnouncementQueries.CanManage(manage, a))).ToList();
    }
}
