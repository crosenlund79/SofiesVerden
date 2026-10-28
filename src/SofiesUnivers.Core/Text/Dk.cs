using System.Globalization;
using System.Text.RegularExpressions;

namespace SofiesUnivers.Core.Text;

/// <summary>
/// Danish formatting and parsing. Implemented explicitly (instead of relying on the
/// da-DK culture) so results are identical in the browser, on a build server and in tests,
/// and so the WebAssembly app does not need to download full ICU globalization data.
/// </summary>
public static partial class Dk
{
    private static readonly string[] MonthNames =
        ["januar", "februar", "marts", "april", "maj", "juni", "juli", "august", "september", "oktober", "november", "december"];

    private static readonly string[] MonthShort =
        ["jan.", "feb.", "mar.", "apr.", "maj", "jun.", "jul.", "aug.", "sep.", "okt.", "nov.", "dec."];

    private static readonly string[] DayNames =
        ["søndag", "mandag", "tirsdag", "onsdag", "torsdag", "fredag", "lørdag"];

    /// <summary>1.234,50 kr.</summary>
    public static string Money(decimal amount) => Amount(amount) + " kr.";

    /// <summary>1.234,50</summary>
    public static string Amount(decimal amount) => SwapSeparators(amount.ToString("N2", CultureInfo.InvariantCulture));

    /// <summary>1.234 (no decimals)</summary>
    public static string Whole(decimal amount) => SwapSeparators(Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture));

    /// <summary>42,5 km / 42 km</summary>
    public static string Km(decimal km) => Number(km) + " km";

    /// <summary>A number with up to two decimals, Danish separators, no trailing zeros: 1.234,5</summary>
    public static string Number(decimal value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("#,##0.##", CultureInfo.InvariantCulture);
        return SwapSeparators(text);
    }

    /// <summary>Amount as typed into an input field: 1234,5 (no thousands separator).</summary>
    public static string InputAmount(decimal amount) =>
        Math.Round(amount, 2).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string SwapSeparators(string invariant)
    {
        var chars = invariant.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch { ',' => '.', '.' => ',', var c => c };
        }

        return new string(chars);
    }

    /// <summary>
    /// Parses an amount typed by a Danish user. Accepts "800", "800,50", "1.250", "1.250,50",
    /// "1250.5", " 800 kr. ". Rejects negative values, more than two decimals and nonsense.
    /// </summary>
    public static bool TryParseAmount(string? input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim().ToLowerInvariant();
        s = s.Replace("kr.", "").Replace("kr", "").Replace("dkk", "").Replace("km", "");
        s = s.Replace(" ", "").Replace(" ", "").Replace("'", "");
        if (s.Length == 0)
        {
            return false;
        }

        string normalized;
        if (s.Contains(','))
        {
            // Danish: '.' groups thousands, ',' is the decimal separator.
            if (s.Count(c => c == ',') > 1 || !DanishPattern().IsMatch(s))
            {
                return false;
            }

            normalized = s.Replace(".", "").Replace(',', '.');
        }
        else if (ThousandsOnlyPattern().IsMatch(s))
        {
            // "1.250" or "12.500.000" — dots group thousands.
            normalized = s.Replace(".", "");
        }
        else if (PlainPattern().IsMatch(s))
        {
            // "1250" or "1250.5" — a single dot is a decimal point.
            normalized = s;
        }
        else
        {
            return false;
        }

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        var dot = normalized.IndexOf('.');
        if (dot >= 0 && normalized.Length - dot - 1 > 2)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    [GeneratedRegex(@"^(\d{1,3}(\.\d{3})+|\d+),\d{0,2}$")]
    private static partial Regex DanishPattern();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial Regex ThousandsOnlyPattern();

    [GeneratedRegex(@"^\d+(\.\d{1,2})?$")]
    private static partial Regex PlainPattern();

    /// <summary>28. okt. 2026</summary>
    public static string Date(DateOnly date) => $"{date.Day}. {MonthShort[date.Month - 1]} {date.Year}";

    /// <summary>28-10-2026</summary>
    public static string DateNumeric(DateOnly date) => $"{date.Day:00}-{date.Month:00}-{date.Year}";

    /// <summary>tirsdag 28. oktober 2026</summary>
    public static string DateLong(DateOnly date) => $"{DayNames[(int)date.DayOfWeek]} {date.Day}. {MonthNames[date.Month - 1]} {date.Year}";

    /// <summary>tirsdag 28. okt.</summary>
    public static string DateWithDay(DateOnly date) => $"{DayNames[(int)date.DayOfWeek]} {date.Day}. {MonthShort[date.Month - 1]}";

    public static string MonthName(int month) => MonthNames[month - 1];

    public static string MonthShortName(int month) => MonthShort[month - 1];

    /// <summary>oktober 2026 (capitalized: Oktober 2026)</summary>
    public static string MonthYear(int year, int month) => Capitalize(MonthNames[month - 1]) + " " + year;

    public static string DayName(DateOnly date) => DayNames[(int)date.DayOfWeek];

    public static string Timestamp(DateTimeOffset timestamp)
    {
        var local = timestamp.ToLocalTime();
        return $"{Date(DateOnly.FromDateTime(local.DateTime))} kl. {local.Hour:00}.{local.Minute:00}";
    }

    /// <summary>2 timer, 2½ timer, 1 time, 1 t. 15 min., 45 min.</summary>
    public static string Duration(int minutes)
    {
        if (minutes <= 0)
        {
            return "0 min.";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0)
        {
            return $"{rest} min.";
        }

        if (rest == 0)
        {
            return hours == 1 ? "1 time" : $"{hours} timer";
        }

        if (rest == 30)
        {
            return $"{hours}½ time{(hours == 1 ? "" : "r")}";
        }

        return $"{hours} t. {rest} min.";
    }

    public static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public static string Count(int count, string singular, string plural) => count == 1 ? $"1 {singular}" : $"{count} {plural}";
}
