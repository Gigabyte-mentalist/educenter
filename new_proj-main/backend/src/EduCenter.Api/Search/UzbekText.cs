using System.Globalization;

namespace EduCenter.Api.Search;

/// <summary>Numbers and dates the way they read in Uzbek. The platform's ICU data has no Uzbek month names (the browser prints "M09 17"), so the months are spelled out here.</summary>
public static class UzbekText
{
    private static readonly string[] Months =
        ["yanvar", "fevral", "mart", "aprel", "may", "iyun", "iyul", "avgust", "sentabr", "oktabr", "noyabr", "dekabr"];

    /// <summary>500000 → "500 000" (grouped with spaces, as the money is written on screen).</summary>
    public static string Money(decimal value) => value.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");

    /// <summary>"18-sentabr".</summary>
    public static string DayMonth(DateOnly date) => $"{date.Day}-{Months[date.Month - 1]}";

    /// <summary>What a person typed as an amount — "500000", "500 000", "500,000" — or null when it is not a number.</summary>
    public static decimal? ParseAmount(string text)
    {
        var digits = new string(text.Where(c => !char.IsWhiteSpace(c) && c != ',' && c != '\u00A0').ToArray());
        return digits.Length > 0 && decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount >= 0
            ? amount
            : null;
    }
}
