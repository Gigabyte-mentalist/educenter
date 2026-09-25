using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Crm.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Crm;

public record MarkTrialAttendanceRequest(bool Attended);

[ApiController]
[Route("api/trial-lessons")]
public class TrialLessonsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "leads.view")]
    public async Task<ActionResult<List<TrialLessonDto>>> List([FromQuery] Guid leadId)
    {
        var scope = HttpContext.RequireResolvedScope();
        var branches = await db.Branches.Where(b => b.OrganizationId == scope.OrganizationId)
            .Select(b => b.Id).ToListAsync();
        var allowed = scope.AllowedBranchIds(branches);
        if (allowed is null) return Forbid();
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == leadId && l.OrganizationId == scope.OrganizationId);
        if (lead is null) return NotFound();
        if (!allowed.Contains(lead.BranchId)) return Forbid();

        return Ok(await db.TrialLessons
            .Where(t => t.OrganizationId == scope.OrganizationId && t.LeadId == leadId)
            .OrderByDescending(t => t.ScheduledAt)
            .Select(t => new TrialLessonDto(t.Id, t.LeadId, t.GroupId, t.ScheduledAt, t.AttendanceStatus))
            .ToListAsync());
    }

    [HttpPatch("{id:guid}/attendance")]
    [Authorize(Policy = "trial.book")]
    public async Task<IActionResult> MarkAttendance(Guid id, MarkTrialAttendanceRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();
        var trial = await db.TrialLessons.FirstOrDefaultAsync(t => t.Id == id && t.OrganizationId == scope.OrganizationId);
        if (trial is null) return NotFound();

        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == trial.LeadId);
        if (lead is null) return NotFound();

        // Same branch rule as every other lead action: a branch-scoped operator may not touch another
        // branch's trial by id.
        var orgBranchIds = await db.Branches
            .Where(b => b.OrganizationId == scope.OrganizationId)
            .Select(b => b.Id)
            .ToListAsync();
        var allowed = scope.AllowedBranchIds(orgBranchIds);
        if (allowed is null || !allowed.Contains(lead.BranchId))
            return Forbid();

        if (lead.ConvertedStudentId is not null || lead.Status == LeadStatus.Converted)
            return Conflict(new { error = "Studentga aylantirilgan lidning trial holatini o'zgartirib bo'lmaydi." });

        if (request.Attended)
        {
            trial.MarkAttended();
            lead.SetStatus(LeadStatus.TrialAttended);
        }
        else
        {
            trial.MarkMissed();
            lead.SetStatus(LeadStatus.TrialMissed);
        }

        await db.SaveChangesAsync();
        return NoContent();
    }
}
