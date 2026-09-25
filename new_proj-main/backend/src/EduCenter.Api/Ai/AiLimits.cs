using System.Text.Json;
using EduCenter.Api.Automation;
using EduCenter.Infrastructure;
using EduCenter.Modules.Ai.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Ai;

public sealed record AiLimitDefinition(string Key, AiFeature Feature, string Label, string Unit, int Min, int Max, int Default);

/// <summary>A user's daily allowance for one feature. For Speaking, units are whole minutes of student audio.</summary>
public sealed record AiLimitState(AiFeature Feature, int Limit, int Used, string Unit)
{
    public bool IsExceeded => Used >= Limit;
    public int Remaining => Math.Max(0, Limit - Used);
}

/// <summary>
/// Daily AI limits (docs/00_TZ.md §54). Defaults live here; an organization stores only its own values in
/// organizations.AiLimitsConfigJson, which are clamped to the catalog range whenever read (like the Automation catalog).
/// </summary>
public static class AiLimitCatalog
{
    public const string TutorRequestsPerDay = "tutorRequestsPerDay";
    public const string WritingChecksPerDay = "writingChecksPerDay";
    public const string SpeakingMinutesPerDay = "speakingMinutesPerDay";

    public static readonly AiLimitDefinition[] All =
    [
        new(TutorRequestsPerDay, AiFeature.Tutor, "AI Tutor so'rovlari", "ta / kun", 0, 1000, 50),
        new(WritingChecksPerDay, AiFeature.Writing, "Writing tekshiruvlari", "ta / kun", 0, 100, 5),
        new(SpeakingMinutesPerDay, AiFeature.Speaking, "Speaking mashqi", "daqiqa / kun", 0, 240, 20)
    ];

    public static AiLimitDefinition? ForFeature(AiFeature feature) => All.FirstOrDefault(d => d.Feature == feature);

    public static Dictionary<string, int> Resolve(string? json)
    {
        Dictionary<string, int> stored;
        try { stored = string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? []; }
        catch (JsonException) { stored = []; }

        return All.ToDictionary(d => d.Key, d => Math.Clamp(stored.TryGetValue(d.Key, out var value) ? value : d.Default, d.Min, d.Max));
    }
}

/// <summary>Counts today's use in the user's branch time zone. Only succeeded requests count — a provider outage or a
/// rejected request does not spend the student's allowance.</summary>
public class AiLimits(AppDbContext db)
{
    public async Task<AiLimitState?> CheckAsync(AiCallContext context, CancellationToken ct = default)
    {
        var definition = AiLimitCatalog.ForFeature(context.Feature);
        if (definition is null) return null;

        var json = await db.Organizations
            .Where(o => o.Id == context.OrganizationId)
            .Select(o => o.AiLimitsConfigJson)
            .FirstOrDefaultAsync(ct);
        var limit = AiLimitCatalog.Resolve(json)[definition.Key];

        var since = BranchClock.StartOfLocalDay(context.BranchTimezone, DateTimeOffset.UtcNow);
        var succeededToday = db.AiRequests.Where(r =>
            r.UserId == context.UserId && r.Feature == context.Feature && r.Status == AiRequestStatus.Succeeded && r.CreatedAt >= since);

        var used = context.Feature == AiFeature.Speaking
            ? (int)Math.Ceiling(await succeededToday.SumAsync(r => r.AudioSeconds, ct) / 60.0)
            : await succeededToday.CountAsync(ct);

        return new AiLimitState(context.Feature, limit, used, definition.Unit);
    }
}
