using System.Security.Claims;
using EduCenter.Api.Authorization;
using EduCenter.Api.Automation;
using EduCenter.Api.Finance;
using EduCenter.Infrastructure;
using EduCenter.Modules.Finance.Domain;
using EduCenter.Modules.Identity.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Search;

public record SearchResultDto(string Type, Guid Id, string Title, string? Subtitle, string Href);

/// <summary>
/// Universal Search (docs/00_TZ.md §27.3, audit §3.18/§5.14): one box, several resource types. There is no
/// single permission for "search" — each type is gated by the same code and scope its own screen already
/// uses, resolved directly through <see cref="PermissionResolver"/> instead of the one-policy-per-endpoint
/// pipeline, so a result never surfaces something the caller's own screens wouldn't. A role that lacks a
/// given permission just gets nothing back for that type, silently — same as opening the screen and seeing
/// a 403 would, minus the dead end.
/// </summary>
[ApiController]
[Route("api/search")]
[Authorize]
public class SearchController(AppDbContext db, PermissionResolver permissions, FinanceQueries finance) : ControllerBase
{
    private const int PerTypeLimit = 5;

    private static readonly Dictionary<PaymentMethod, string> MethodLabel = new()
    {
        [PaymentMethod.Cash] = "Naqd", [PaymentMethod.Card] = "Karta", [PaymentMethod.BankTransfer] = "Bank o'tkazmasi",
        [PaymentMethod.Click] = "Click", [PaymentMethod.Payme] = "Payme", [PaymentMethod.Other] = "Boshqa",
    };

    [HttpGet]
    public async Task<ActionResult<List<SearchResultDto>>> Search([FromQuery] string? q, CancellationToken ct = default)
    {
        var term = q?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < 2)
            return Ok(new List<SearchResultDto>());

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
            return Unauthorized();

        var like = $"%{term}%";
        var results = new List<SearchResultDto>();

        await AddStudentsAsync(results, userId, like, ct);
        await AddTeachersAsync(results, userId, like, ct);
        await AddGroupsAndCoursesAsync(results, userId, like, ct);
        await AddLeadsAsync(results, userId, like, ct);
        await AddBranchesAsync(results, userId, like, ct);
        await AddSupportCasesAsync(results, userId, like, ct);
        await AddPaymentsAsync(results, userId, term, like, ct);

        return Ok(results);
    }

    /// <summary>
    /// Payments, by amount ("500000" and "500 000" are the same number), by the student's name, or by the note on the payment. Same gate and the same branch cut
    /// as the finance screen (<see cref="FinanceQueries.ScopedPaymentsAsync"/>). Only the staff who work the finance screen get this type: a student's or a
    /// parent's own payments are read on their profile, and a result that opens <c>/finance</c> for someone with no such page would be a dead end.
    /// </summary>
    private async Task AddPaymentsAsync(List<SearchResultDto> results, Guid userId, string term, string like, CancellationToken ct)
    {
        var scope = await permissions.ResolveAsync(userId, "payments.view", ct);
        if (scope is not { Scope: ScopeType.AllOrganization or ScopeType.MyBranches }) return;

        var payments = await finance.ScopedPaymentsAsync(scope);
        var amount = UzbekText.ParseAmount(term) ?? -1m;   // no payment has a negative amount, so "not a number" matches nothing

        var rows = await (
            from p in payments
            join s in db.Students on p.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            join b in db.Branches on p.BranchId equals b.Id
            where p.Amount == amount || EF.Functions.ILike(u.FullName, like) || (p.Note != null && EF.Functions.ILike(p.Note, like))
            orderby p.PaidAt descending
            select new { p.Id, p.Amount, p.PaidAt, p.Method, p.IsVoided, u.FullName, b.Timezone })
            .Take(PerTypeLimit).ToListAsync(ct);

        results.AddRange(rows.Select(r => new SearchResultDto("payment", r.Id,
            $"{UzbekText.Money(r.Amount)} so'm — {r.FullName}",
            r.IsVoided ? "Bekor qilingan" : $"{UzbekText.DayMonth(BranchClock.LocalDate(r.Timezone, r.PaidAt))} · {MethodLabel.GetValueOrDefault(r.Method, "Boshqa")}",
            $"/finance?highlight={r.Id}")));
    }

    private async Task AddStudentsAsync(List<SearchResultDto> results, Guid userId, string like, CancellationToken ct)
    {
        var scope = await permissions.ResolveAsync(userId, "students.view", ct);
        if (scope is null) return;
        var scopedIds = scope.ScopedStudentIds(db);
        if (scopedIds is null) return;

        var matches = await (
            from s in db.Students
            join u in db.Users on s.UserId equals u.Id
            where scopedIds.Contains(s.Id) &&
                (EF.Functions.ILike(u.FullName, like) || EF.Functions.ILike(u.Email, like) || EF.Functions.ILike(s.StudentNumber, like))
            orderby u.FullName
            select new SearchResultDto("student", s.Id, u.FullName, s.StudentNumber, $"/students/{s.Id}"))
            .Take(PerTypeLimit).ToListAsync(ct);
        results.AddRange(matches);
    }

    /// <summary>Mirrors UsersController.List's own scope switch — the same roles that can list staff can
    /// search them, and MyGroups/AssignedGroups/MyChildren get no flat listing there either.</summary>
    private async Task AddTeachersAsync(List<SearchResultDto> results, Guid userId, string like, CancellationToken ct)
    {
        var scope = await permissions.ResolveAsync(userId, "users.view", ct);
        if (scope is null) return;

        var teacherRoleId = await db.Roles.Where(r => r.Code == RoleCode.Teacher).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var query = db.Users.Where(u => u.OrganizationId == scope.OrganizationId && u.Roles.Any(ur => ur.RoleId == teacherRoleId));
        query = scope.Scope switch
        {
            ScopeType.AllOrganization => query,
            ScopeType.MyBranches => query.Where(u => u.Branches.Any(b => scope.BranchIds.Contains(b.BranchId))),
            ScopeType.Self => query.Where(u => u.Id == scope.UserId),
            _ => query.Where(_ => false),
        };

        var matches = await query
            .Where(u => EF.Functions.ILike(u.FullName, like) || EF.Functions.ILike(u.Email, like))
            .OrderBy(u => u.FullName)
            .Select(u => new SearchResultDto("teacher", u.Id, u.FullName, u.Email, $"/teachers/{u.Id}"))
            .Take(PerTypeLimit).ToListAsync(ct);
        results.AddRange(matches);
    }

    private async Task AddGroupsAndCoursesAsync(List<SearchResultDto> results, Guid userId, string like, CancellationToken ct)
    {
        var scope = await permissions.ResolveAsync(userId, "groups.view", ct);
        if (scope is null) return;

        var groups = await scope.ScopedGroupsAsync(db);
        var groupMatches = await (
            from g in groups
            join c in db.Courses on g.CourseId equals c.Id
            where EF.Functions.ILike(g.Name, like)
            orderby g.Name
            select new SearchResultDto("group", g.Id, g.Name, c.Name, $"/groups/{g.Id}"))
            .Take(PerTypeLimit).ToListAsync(ct);
        results.AddRange(groupMatches);

        // Course catalog visibility follows group visibility (CoursesController) — same gate, no further
        // branch scoping, because a course isn't tied to one branch.
        var courseMatches = await db.Courses
            .Where(c => c.OrganizationId == scope.OrganizationId && EF.Functions.ILike(c.Name, like))
            .OrderBy(c => c.Name)
            .Select(c => new SearchResultDto("course", c.Id, c.Name, c.LevelCategory, "/groups"))
            .Take(PerTypeLimit).ToListAsync(ct);
        results.AddRange(courseMatches);
    }

    private async Task AddLeadsAsync(List<SearchResultDto> results, Guid userId, string like, CancellationToken ct)
    {
        var scope = await permissions.ResolveAsync(userId, "leads.view", ct);
        if (scope is null) return;
        var allowed = scope.AllowedBranchIds(await db.Branches.Where(b => b.OrganizationId == scope.OrganizationId).Select(b => b.Id).ToListAsync(ct));
        if (allowed is null) return;

        var matches = await db.Leads
            .Where(l => l.OrganizationId == scope.OrganizationId && allowed.Contains(l.BranchId) &&
                (EF.Functions.ILike(l.FullName, like) || EF.Functions.ILike(l.Phone, like)))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new SearchResultDto("lead", l.Id, l.FullName, l.Phone, $"/leads/{l.Id}"))
            .Take(PerTypeLimit).ToListAsync(ct);
        results.AddRange(matches);
    }

    private async Task AddBranchesAsync(List<SearchResultDto> results, Guid userId, string like, CancellationToken ct)
    {
        var scope = await permissions.ResolveAsync(userId, "branch.view", ct);
        if (scope is null) return;
        var allowed = scope.AllowedBranchIds(await db.Branches.Where(b => b.OrganizationId == scope.OrganizationId).Select(b => b.Id).ToListAsync(ct));
        if (allowed is null) return;

        var matches = await db.Branches
            .Where(b => allowed.Contains(b.Id) && EF.Functions.ILike(b.Name, like))
            .OrderBy(b => b.Name)
            .Select(b => new SearchResultDto("branch", b.Id, b.Name, b.Address, $"/branches?highlight={b.Id}"))
            .Take(PerTypeLimit).ToListAsync(ct);
        results.AddRange(matches);
    }

    private async Task AddSupportCasesAsync(List<SearchResultDto> results, Guid userId, string like, CancellationToken ct)
    {
        var scope = await permissions.ResolveAsync(userId, "support.recommend.view", ct);
        if (scope is null) return;

        var groups = await scope.ScopedGroupsAsync(db);
        var matches = await (
            from c in db.SupportCases
            join g in groups on c.GroupId equals g.Id
            join s in db.Students on c.StudentId equals s.Id
            join u in db.Users on s.UserId equals u.Id
            where EF.Functions.ILike(u.FullName, like) || EF.Functions.ILike(c.Reason, like)
            orderby c.CreatedAt descending
            select new SearchResultDto("ticket", c.Id, u.FullName, c.Reason, $"/support?highlight={c.Id}"))
            .Take(PerTypeLimit).ToListAsync(ct);
        results.AddRange(matches);
    }
}
