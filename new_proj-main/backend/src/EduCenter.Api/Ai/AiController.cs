using System.Security.Claims;
using System.Text.Json;
using EduCenter.Api.Ai.Providers;
using EduCenter.Api.Authorization;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai;

public record AiFeatureStatusDto(AiFeature Feature, bool Allowed, bool Available, int? Limit, int? Used, int? Remaining, string? Unit);

/// <param name="DemoMode">True when an allowed feature would be answered by the fake provider — the UI says so.</param>
public record AiStatusDto(bool DemoMode, List<AiFeatureStatusDto> Features);

public record AiLimitDto(string Key, AiFeature Feature, string Label, string Unit, int Min, int Max, int Default, int Value);
public record AiLimitsDto(bool IsCustomized, List<AiLimitDto> Limits);
public record UpdateAiLimitsRequest(Dictionary<string, int>? Limits);

public record AiRouteDto(AiTaskKind Task, List<string> Chain);
public record AiProvidersDto(List<AiProviderInfo> Providers, List<AiRouteDto> Routes);
public record AiProviderCheckDto(bool Ok, string? Model, int LatencyMs, string? Error);

/// <summary>AI Gateway surface (docs/06_phase4_technical_plan.md Sprint 1): what the caller may use today, the
/// organization's daily limits, and the provider/route configuration with a connectivity check.</summary>
[ApiController]
[Route("api/ai")]
public class AiController(
    AppDbContext db, PermissionResolver permissions, AiProviderRegistry providers, AiLimits limits,
    AiCallContextFactory contexts, AiGateway gateway) : ControllerBase
{
    private static readonly (AiFeature Feature, string Permission, AiTaskKind Task)[] StudentFeatures =
    [
        (AiFeature.Tutor, "ai.tutor.use", AiTaskKind.TutorAnswer),
        (AiFeature.Writing, "ai.writing.use", AiTaskKind.WritingEvaluation),
        (AiFeature.Speaking, "ai.speaking.use", AiTaskKind.SpeakingReply)
    ];

    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<AiStatusDto>> Status(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();

        var features = new List<AiFeatureStatusDto>();
        var demoMode = false;
        foreach (var (feature, permission, task) in StudentFeatures)
        {
            var allowed = await permissions.ResolveAsync(userId, permission, ct) is not null;
            var route = providers.Route(task);
            if (!allowed)
            {
                features.Add(new AiFeatureStatusDto(feature, false, route.Count > 0, null, null, null, null));
                continue;
            }

            demoMode |= route.FirstOrDefault()?.Provider is FakeChatModelProvider;
            var context = await contexts.ForUserAsync(userId, feature, ct);
            var limit = context is null ? null : await limits.CheckAsync(context, ct);
            features.Add(new AiFeatureStatusDto(feature, true, route.Count > 0, limit?.Limit, limit?.Used, limit?.Remaining, limit?.Unit));
        }

        return Ok(new AiStatusDto(demoMode, features));
    }

    [HttpGet("limits")]
    [Authorize(Policy = "ai.usage.view")]
    public async Task<ActionResult<AiLimitsDto>> Limits(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var json = await db.Organizations.Where(o => o.Id == scope.OrganizationId).Select(o => o.AiLimitsConfigJson).FirstOrDefaultAsync(ct);
        return Ok(BuildLimits(json));
    }

    /// <summary>Omitted keys keep their current value; every value must lie in the catalog range.</summary>
    [HttpPut("limits")]
    [Authorize(Policy = "ai.limits.edit")]
    public async Task<ActionResult<AiLimitsDto>> UpdateLimits(UpdateAiLimitsRequest request, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == scope.OrganizationId, ct);
        if (organization is null) return NotFound();

        var given = request.Limits ?? [];
        var unknown = given.Keys.FirstOrDefault(k => AiLimitCatalog.All.All(d => d.Key != k));
        if (unknown is not null) return BadRequest(new { error = $"Noma'lum limit: {unknown}." });

        var values = AiLimitCatalog.Resolve(organization.AiLimitsConfigJson);
        foreach (var definition in AiLimitCatalog.All)
        {
            if (!given.TryGetValue(definition.Key, out var value)) continue;
            if (value < definition.Min || value > definition.Max)
                return BadRequest(new { error = $"«{definition.Label}» {definition.Min} dan {definition.Max} gacha bo'lishi kerak." });
            values[definition.Key] = value;
        }

        organization.SetAiLimits(JsonSerializer.Serialize(values));
        await db.SaveChangesAsync(ct);
        return Ok(BuildLimits(organization.AiLimitsConfigJson));
    }

    /// <summary>Back to the catalog defaults.</summary>
    [HttpDelete("limits")]
    [Authorize(Policy = "ai.limits.edit")]
    public async Task<ActionResult<AiLimitsDto>> ResetLimits(CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == scope.OrganizationId, ct);
        if (organization is null) return NotFound();

        if (organization.AiLimitsConfigJson is not null)
        {
            organization.SetAiLimits(null);
            await db.SaveChangesAsync(ct);
        }

        return Ok(BuildLimits(null));
    }

    /// <summary>Configured providers (API keys are never returned — only whether one is set) and the resolved route per task.</summary>
    [HttpGet("providers")]
    [Authorize(Policy = "ai.usage.view")]
    public ActionResult<AiProvidersDto> Providers() =>
        Ok(new AiProvidersDto(
            providers.Providers.ToList(),
            Enum.GetValues<AiTaskKind>()
                .Select(task => new AiRouteDto(task, task == AiTaskKind.SpeechSynthesis
                    ? providers.SynthesisRoute().Select(t => $"{t.Provider.Name}:{t.Voice}").ToList()
                    : providers.Route(task).Select(t => $"{t.Provider.Name}:{t.Model}").ToList()))
                .ToList()));

    [HttpPost("providers/{name}/check")]
    [Authorize(Policy = "ai.limits.edit")]
    [EnableRateLimiting(AiGateway.RateLimitPolicy)]
    public async Task<ActionResult<AiProviderCheckDto>> CheckProvider(string name, CancellationToken ct)
    {
        var scope = HttpContext.RequireResolvedScope();
        if (providers.Providers.All(p => !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return NotFound(new { error = "Bunday provider yo'q." });

        var context = await contexts.ForUserAsync(scope.UserId, AiFeature.System, ct);
        if (context is null) return Unauthorized();

        var result = await gateway.CheckProviderAsync(context, name, ct);
        return Ok(new AiProviderCheckDto(result.IsSuccess, result.Model, result.LatencyMs, result.Error));
    }

    private static AiLimitsDto BuildLimits(string? json)
    {
        var values = AiLimitCatalog.Resolve(json);
        return new AiLimitsDto(json is not null, AiLimitCatalog.All
            .Select(d => new AiLimitDto(d.Key, d.Feature, d.Label, d.Unit, d.Min, d.Max, d.Default, values[d.Key]))
            .ToList());
    }
}
