namespace EduCenter.Modules.Crm.Domain;

/// <summary>The times of day a lead can attend (docs/00_TZ.md §11 "preferred time"). A lead may pick several; an empty
/// choice means "any time". Kept as codes in one text column rather than an enum array.</summary>
public static class PreferredTimeCodes
{
    public const string Morning = "Morning";     // starts 08:00–12:00
    public const string Afternoon = "Afternoon"; // starts 12:00–17:00
    public const string Evening = "Evening";     // starts 17:00–21:00
    public const string Weekend = "Weekend";     // Saturday or Sunday, any hour

    public static readonly IReadOnlyList<string> All = [Morning, Afternoon, Evening, Weekend];

    public const int MaxLength = 100;

    /// <summary>Parses "evening, weekend" into the canonical "Evening,Weekend" (fixed order, no duplicates). Blank → null (no
    /// preference). Returns false when a code is unknown.</summary>
    public static bool TryNormalize(string? raw, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        var picked = new HashSet<string>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = All.FirstOrDefault(c => c.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (code is null) return false;
            picked.Add(code);
        }

        normalized = picked.Count == 0 ? null : string.Join(',', All.Where(picked.Contains));
        return true;
    }

    public static IReadOnlyCollection<string> Split(string? stored) =>
        string.IsNullOrWhiteSpace(stored) ? [] : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
