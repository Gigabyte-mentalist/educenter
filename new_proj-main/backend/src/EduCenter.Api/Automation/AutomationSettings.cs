using EduCenter.Infrastructure;
using EduCenter.Modules.Automation.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduCenter.Api.Automation;

/// <summary>Reads an organization's effective rule settings, cached for the lifetime of the request or job run.</summary>
public class AutomationSettings(AppDbContext db)
{
    private readonly Dictionary<(Guid OrganizationId, string Code), EffectiveRule> _cache = [];

    public async Task<EffectiveRule> GetAsync(Guid organizationId, string code, CancellationToken ct = default)
    {
        if (_cache.TryGetValue((organizationId, code), out var cached)) return cached;

        var row = await db.AutomationRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.OrganizationId == organizationId && r.Code == code, ct);
        var effective = AutomationCatalog.Get(code).Resolve(row);

        _cache[(organizationId, code)] = effective;
        return effective;
    }
}

/// <summary>Records what a rule did. Adds the row to the context only; the rule's own SaveChanges commits it together
/// with the tasks/recommendations/messages it describes.</summary>
public class AutomationJournal(AppDbContext db)
{
    /// <summary>Everything recorded through this instance (one request or job run) — what "run now" reports.</summary>
    public int RecordedItems { get; private set; }

    public void Record(Guid organizationId, string ruleCode, AutomationTrigger trigger, int itemCount, string summary)
    {
        if (itemCount <= 0) return;
        db.AutomationRunLogs.Add(new AutomationRunLog(organizationId, ruleCode, trigger, itemCount, summary));
        RecordedItems += itemCount;
    }

    public static string Describe(params (int Count, string Label)[] parts) =>
        string.Join(", ", parts.Where(p => p.Count > 0).Select(p => $"{p.Count} {p.Label}"));
}
