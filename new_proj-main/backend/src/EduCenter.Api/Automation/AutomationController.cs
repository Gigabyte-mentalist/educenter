using System.Text.Json;
using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Automation.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Automation;

/// <param name="Scale">Min, Max, Default and Value are whole numbers in stored units; divide by this to show them (a band of 5.5 is 55 with a scale of 10).</param>
public record AutomationParameterDto(string Key, string Label, string Unit, int Min, int Max, int Default, int Value, int Scale);
public record AutomationActionDto(string Key, string Label, bool Default, bool Value);
public record AutomationRunDto(Guid Id, string RuleCode, string RuleName, AutomationTrigger Trigger, int ItemCount, string Summary, DateTimeOffset RanAt);

public record AutomationRuleDto(
    string Code, string Name, string Trigger, string Description, bool Enabled, bool IsCustomized,
    List<AutomationParameterDto> Parameters, List<AutomationActionDto> Actions,
    DateTimeOffset? UpdatedAt, string? UpdatedByName, AutomationRunDto? LastRun);

public record UpdateAutomationRuleRequest(bool Enabled, Dictionary<string, int>? Parameters, Dictionary<string, bool>? Actions);

public record AutomationRunNowDto(
    int InvoicesIssued, int StudentsMarkedDebt, int StudentsCleared, int DebtTasks,
    int TrialFollowUps, int AbsenceSignals, int HomeworkSignals, int Reminders, int RuleActions, int LearningSignals = 0);

/// <summary>Automation Engine settings (docs/00_TZ.md §40). Rules are organization-wide: branch administrators may look,
/// only an organization-wide manager (automation.manage) changes thresholds and actions.</summary>
[ApiController]
[Route("api/automation")]
public class AutomationController(AppDbContext db, AutomationRunner runner) : ControllerBase
{
    [HttpGet("rules")]
    [Authorize(Policy = "automation.view")]
    public async Task<ActionResult<List<AutomationRuleDto>>> Rules()
    {
        var scope = HttpContext.RequireResolvedScope();
        return Ok(await BuildRulesAsync(scope.OrganizationId));
    }

    [HttpPut("rules/{code}")]
    [Authorize(Policy = "automation.manage")]
    public async Task<ActionResult<AutomationRuleDto>> UpdateRule(string code, UpdateAutomationRuleRequest request)
    {
        var scope = HttpContext.RequireResolvedScope();

        var definition = AutomationCatalog.Find(code);
        if (definition is null) return NotFound(new { error = "Bunday qoida yo'q." });

        var unknownParameter = request.Parameters?.Keys.FirstOrDefault(k => definition.Parameters.All(p => p.Key != k));
        if (unknownParameter is not null) return BadRequest(new { error = $"Noma'lum parametr: {unknownParameter}." });
        var unknownAction = request.Actions?.Keys.FirstOrDefault(k => definition.Actions.All(a => a.Key != k));
        if (unknownAction is not null) return BadRequest(new { error = $"Noma'lum amal: {unknownAction}." });

        var row = await db.AutomationRules.FirstOrDefaultAsync(r => r.OrganizationId == scope.OrganizationId && r.Code == code);
        var current = definition.Resolve(row);

        // Omitted keys keep their current value, so a client may send only what it changes.
        var parameters = new Dictionary<string, int>();
        foreach (var parameter in definition.Parameters)
        {
            var value = request.Parameters is not null && request.Parameters.TryGetValue(parameter.Key, out var given)
                ? given
                : current.Param(parameter.Key);
            if (value < parameter.Min || value > parameter.Max)
                return BadRequest(new { error = $"«{parameter.Label}» {Shown(parameter.Min, parameter.Scale)} dan {Shown(parameter.Max, parameter.Scale)} gacha bo'lishi kerak." });
            parameters[parameter.Key] = value;
        }

        var actions = definition.Actions.ToDictionary(
            a => a.Key,
            a => request.Actions is not null && request.Actions.TryGetValue(a.Key, out var given) ? given : current.Action(a.Key));

        var problem = definition.Validate(parameters);
        if (problem is not null) return BadRequest(new { error = problem });

        var parametersJson = JsonSerializer.Serialize(parameters);
        var actionsJson = JsonSerializer.Serialize(actions);
        if (row is null)
            db.AutomationRules.Add(new AutomationRule(scope.OrganizationId, code, request.Enabled, parametersJson, actionsJson, scope.UserId));
        else
            row.Update(request.Enabled, parametersJson, actionsJson, scope.UserId);

        await db.SaveChangesAsync();
        return Ok((await BuildRulesAsync(scope.OrganizationId)).Single(r => r.Code == code));
    }

    /// <summary>Back to catalog defaults: the organization's row is deleted (the deletion is audited).</summary>
    [HttpDelete("rules/{code}")]
    [Authorize(Policy = "automation.manage")]
    public async Task<ActionResult<AutomationRuleDto>> ResetRule(string code)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (AutomationCatalog.Find(code) is null) return NotFound(new { error = "Bunday qoida yo'q." });

        var row = await db.AutomationRules.FirstOrDefaultAsync(r => r.OrganizationId == scope.OrganizationId && r.Code == code);
        if (row is not null)
        {
            db.AutomationRules.Remove(row);
            await db.SaveChangesAsync();
        }

        return Ok((await BuildRulesAsync(scope.OrganizationId)).Single(r => r.Code == code));
    }

    [HttpGet("runs")]
    [Authorize(Policy = "automation.view")]
    public async Task<ActionResult<List<AutomationRunDto>>> Runs([FromQuery] int limit = 50)
    {
        var scope = HttpContext.RequireResolvedScope();
        return Ok(await RecentRunsAsync(scope.OrganizationId, Math.Clamp(limit, 1, 200)));
    }

    /// <summary>Runs every rule for the caller's organization now, instead of waiting for the next scheduled run.</summary>
    [HttpPost("run")]
    [Authorize(Policy = "automation.manage")]
    public async Task<ActionResult<AutomationRunNowDto>> RunNow(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var result = await runner.RunAsync(DateTimeOffset.UtcNow, scope.OrganizationId, AutomationTrigger.Manual, ct);

        return Ok(new AutomationRunNowDto(
            result.Billing.InvoicesIssued, result.Billing.StudentsMarkedDebt, result.Billing.StudentsCleared, result.Billing.DebtTasks,
            result.FollowUps.TrialFollowUps, result.FollowUps.AbsenceSignals, result.FollowUps.HomeworkSignals, result.Notifications.Total,
            result.RuleActions, result.FollowUps.LearningSignals));
    }

    private static string Shown(int stored, int scale) =>
        scale == 1 ? stored.ToString() : (stored / (decimal)scale).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<List<AutomationRuleDto>> BuildRulesAsync(Guid organizationId)
    {
        var rows = await db.AutomationRules.AsNoTracking().Where(r => r.OrganizationId == organizationId).ToListAsync();
        var editorIds = rows.Select(r => r.UpdatedBy).Distinct().ToList();
        var editors = await db.Users.Where(u => editorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var runs = await RecentRunsAsync(organizationId, 300);

        return AutomationCatalog.All
            .Select(definition =>
            {
                var row = rows.FirstOrDefault(r => r.Code == definition.Code);
                var effective = definition.Resolve(row);
                return new AutomationRuleDto(
                    definition.Code, definition.Name, definition.Trigger, definition.Description, effective.Enabled, row is not null,
                    definition.Parameters.Select(p => new AutomationParameterDto(p.Key, p.Label, p.Unit, p.Min, p.Max, p.Default, effective.Param(p.Key), p.Scale)).ToList(),
                    definition.Actions.Select(a => new AutomationActionDto(a.Key, a.Label, a.Default, effective.Action(a.Key))).ToList(),
                    row is null ? null : row.UpdatedAt ?? row.CreatedAt,
                    row is null ? null : editors.GetValueOrDefault(row.UpdatedBy),
                    runs.FirstOrDefault(r => r.RuleCode == definition.Code));
            })
            .ToList();
    }

    private async Task<List<AutomationRunDto>> RecentRunsAsync(Guid organizationId, int limit)
    {
        var rows = await db.AutomationRunLogs.AsNoTracking()
            .Where(r => r.OrganizationId == organizationId)
            .OrderByDescending(r => r.RanAt)
            .Take(limit)
            .ToListAsync();

        return rows
            .Select(r => new AutomationRunDto(r.Id, r.RuleCode, AutomationCatalog.Find(r.RuleCode)?.Name ?? r.RuleCode,
                r.Trigger, r.ItemCount, r.Summary, r.RanAt))
            .ToList();
    }
}
