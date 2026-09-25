using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Organizations.Domain;
using EduCenter.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Organizations;

public record BranchDto(Guid Id, string Name, string Address, string Timezone);
public record CreateBranchRequest(string Name, string Address, string Timezone);
public record UpdateBranchRequest(string Name, string Address, string Timezone);

[ApiController]
[Route("api/branches")]
public class BranchesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "branch.view")]
    public async Task<ActionResult<List<BranchDto>>> List()
    {
        var scope = HttpContext.RequireResolvedScope();

        var orgBranchIds = await db.Branches
            .Where(b => b.OrganizationId == scope.OrganizationId)
            .Select(b => b.Id)
            .ToListAsync();

        var allowed = scope.AllowedBranchIds(orgBranchIds);
        if (allowed is null)
            return Forbid();

        var branches = await db.Branches
            .Where(b => b.OrganizationId == scope.OrganizationId && allowed.Contains(b.Id))
            .Select(b => new BranchDto(b.Id, b.Name, b.Address, b.Timezone))
            .ToListAsync();

        return Ok(branches);
    }

    [HttpPost]
    [Authorize(Policy = "branch.create")]
    public async Task<ActionResult<BranchDto>> Create(CreateBranchRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization)
            return Forbid();

        var org = await db.Organizations.FindAsync(scope.OrganizationId);
        if (org is null) return NotFound("Organization not found.");

        var branch = org.AddBranch(request.Name, request.Address, request.Timezone);
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new BranchDto(branch.Id, branch.Name, branch.Address, branch.Timezone));
    }

    [HttpPatch("{id:guid}")]
    [Authorize(Policy = "branch.edit")]
    public async Task<ActionResult<BranchDto>> Update(Guid id, UpdateBranchRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var orgBranchIds = await db.Branches
            .Where(b => b.OrganizationId == scope.OrganizationId)
            .Select(b => b.Id)
            .ToListAsync();
        var allowed = scope.AllowedBranchIds(orgBranchIds);

        if (allowed is null || !allowed.Contains(id))
            return Forbid();

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id);
        if (branch is null) return NotFound();

        branch.Update(request.Name, request.Address, request.Timezone);
        await db.SaveChangesAsync();

        return Ok(new BranchDto(branch.Id, branch.Name, branch.Address, branch.Timezone));
    }

    /// <summary>
    /// Removes an empty branch. A branch is the container groups, lessons, invoices and staff assignments hang
    /// off, and none of those carry a database-level foreign key back to it, so deleting a branch in use would
    /// silently orphan them rather than fail. Everything attached is counted first and the delete is refused
    /// with the counts, which is also the answer to "why can't I delete it" without a trip to the logs.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "branch.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (scope.Scope != ScopeType.AllOrganization)
            return Forbid();

        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id && b.OrganizationId == scope.OrganizationId, ct);
        if (branch is null) return NotFound();

        if (await db.Branches.CountAsync(b => b.OrganizationId == scope.OrganizationId, ct) <= 1)
            return Conflict(new { error = "Oxirgi filialni o'chirib bo'lmaydi — tashkilotda kamida bitta filial bo'lishi kerak." });

        var blockers = new List<string>();
        var groups = await db.Groups.CountAsync(g => g.BranchId == id, ct);
        if (groups > 0) blockers.Add($"{groups} ta guruh");
        var lessons = await db.Lessons.CountAsync(l => l.BranchId == id, ct);
        if (lessons > 0) blockers.Add($"{lessons} ta dars");
        var staff = await db.UserBranches.CountAsync(u => u.BranchId == id, ct);
        if (staff > 0) blockers.Add($"{staff} ta biriktirilgan foydalanuvchi");
        var leads = await db.Leads.CountAsync(l => l.BranchId == id, ct);
        if (leads > 0) blockers.Add($"{leads} ta lead");

        if (blockers.Count > 0)
            return Conflict(new
            {
                error = $"Filialni o'chirib bo'lmaydi — unda hali {string.Join(", ", blockers)} bor. "
                    + "Avval ularni boshqa filialga ko'chiring yoki yoping.",
                blockers,
            });

        db.Branches.Remove(branch);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
