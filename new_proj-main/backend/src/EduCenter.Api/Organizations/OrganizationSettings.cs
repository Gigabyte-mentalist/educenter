using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EduCenter.Api.Organizations;

/// <summary>What an organization stores in <c>organizations.SettingsJson</c>: only the values it has changed.</summary>
public sealed record OrganizationSettingsValues(string? Currency, string? WorkdayStart, string? WorkdayEnd);

/// <summary>What an organization stores in <c>organizations.BrandingConfigJson</c>. The logo is a random storage key, never a file name.</summary>
public sealed record OrganizationBranding(string? BrandColor, string? LogoKey);

/// <summary>The settings after the catalog has been applied — every field has a value.</summary>
public sealed record ResolvedOrganizationSettings(string Currency, TimeOnly WorkdayStart, TimeOnly WorkdayEnd);

/// <summary>
/// Organization-level configuration (docs/00_TZ.md §48). Same shape as <c>AiLimitCatalog</c>: defaults and allowed values live
/// here, an organization stores only what it changed, and anything unknown or out of range is ignored when read — a hand-edited
/// row cannot break a page.
/// </summary>
public static partial class OrganizationSettingsCatalog
{
    public const string DefaultCurrency = "UZS";
    public static readonly TimeOnly DefaultWorkdayStart = new(8, 0);
    public static readonly TimeOnly DefaultWorkdayEnd = new(21, 0);

    /// <summary><c>formatMoney</c> on the client knows how to write these; a code outside the list would print as a bare three letters.</summary>
    public static readonly string[] Currencies = ["UZS", "USD", "EUR", "RUB"];

    /// <summary>White text has to stay readable on the brand colour (WCAG AA, 4.5:1) — it fills buttons, badges and the sidebar mark.</summary>
    public const double MinimumWhiteContrast = 4.5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexPattern();

    public static bool IsHexColor(string? value) => value is not null && HexPattern().IsMatch(value);

    public static bool IsCurrency(string? value) => value is not null && Currencies.Contains(value);

    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static string FormatTime(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static ResolvedOrganizationSettings Resolve(string? json)
    {
        var stored = Read<OrganizationSettingsValues>(json);
        var start = TryParseTime(stored?.WorkdayStart, out var s) ? s : DefaultWorkdayStart;
        var end = TryParseTime(stored?.WorkdayEnd, out var e) ? e : DefaultWorkdayEnd;
        if (end <= start) (start, end) = (DefaultWorkdayStart, DefaultWorkdayEnd);
        return new ResolvedOrganizationSettings(IsCurrency(stored?.Currency) ? stored!.Currency! : DefaultCurrency, start, end);
    }

    public static string Serialize(ResolvedOrganizationSettings settings) =>
        JsonSerializer.Serialize(new OrganizationSettingsValues(settings.Currency, FormatTime(settings.WorkdayStart), FormatTime(settings.WorkdayEnd)), Json);

    public static OrganizationBranding ResolveBranding(string? json)
    {
        var stored = Read<OrganizationBranding>(json);
        return new OrganizationBranding(IsHexColor(stored?.BrandColor) ? stored!.BrandColor!.ToLowerInvariant() : null, stored?.LogoKey);
    }

    /// <summary>Null when both parts are empty — the column goes back to null rather than holding <c>{}</c>.</summary>
    public static string? SerializeBranding(OrganizationBranding branding) =>
        branding.BrandColor is null && branding.LogoKey is null ? null : JsonSerializer.Serialize(branding, Json);

    /// <summary>Contrast ratio of white text on <paramref name="hex"/> (WCAG 2.x relative luminance).</summary>
    public static double WhiteContrast(string hex)
    {
        static double Channel(int value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var r = Convert.ToInt32(hex.Substring(1, 2), 16);
        var g = Convert.ToInt32(hex.Substring(3, 2), 16);
        var b = Convert.ToInt32(hex.Substring(5, 2), 16);
        var luminance = 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
        return 1.05 / (luminance + 0.05);
    }

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); }
        catch (JsonException) { return null; }
    }
}
