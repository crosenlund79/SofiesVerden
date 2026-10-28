using System.Text;

namespace SofiesUnivers.Core.Text;

public static class Names
{
    public const int MaxLookupLength = 60;
    public const int MaxNicknameLength = 80;
    public const int MaxDescriptionLength = 500;

    /// <summary>Trims and collapses internal whitespace. Returns null for empty input.</summary>
    public static string? Clean(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var sb = new StringBuilder(input.Length);
        var lastWasSpace = false;
        foreach (var c in input.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                }

                lastWasSpace = true;
            }
            else if (!char.IsControl(c))
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>Key used to detect duplicates that only differ by case or whitespace.</summary>
    public static string Key(string name) => (Clean(name) ?? string.Empty).ToUpperInvariant();

    public static bool SameName(string a, string b) => Key(a) == Key(b);
}

/// <summary>Sorts text in Danish alphabetical order (æ, ø, å after z).</summary>
public sealed class DanishComparer : IComparer<string?>
{
    public static readonly DanishComparer Instance = new();

    public int Compare(string? x, string? y) => string.CompareOrdinal(SortKey(x), SortKey(y));

    private static string SortKey(string? s)
    {
        if (s is null)
        {
            return string.Empty;
        }

        var lower = s.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            sb.Append(c switch
            {
                'æ' or 'ä' => "{1",
                'ø' or 'ö' => "{2",
                'å' => "{3",
                'é' => "e",
                _ => c.ToString(),
            });
        }

        return sb.ToString();
    }
}
