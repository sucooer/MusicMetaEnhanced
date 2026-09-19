using System.Text;

namespace Emby.Plugin.AppleMusic.Utils;

/// <summary>
/// Compares names coming from different metadata sources.
/// Emby merges metadata of several providers, so a fuzzy match here would silently
/// pollute the library with data of the wrong album or artist. Only an exact match
/// (ignoring case, whitespace and punctuation) is accepted.
/// </summary>
public static class TitleMatcher
{
    /// <summary>
    /// Gets a value indicating whether two titles refer to the same item.
    /// </summary>
    /// <param name="first">First title.</param>
    /// <param name="second">Second title.</param>
    /// <returns>True when both titles normalize to the same value.</returns>
    public static bool IsSameTitle(string? first, string? second)
    {
        var left = Normalize(first);
        var right = Normalize(second);
        return left.Length > 0 && string.Equals(left, right, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets a value indicating whether two titles are literally the same name, i.e. without
    /// kana romanization. Romaji equivalence ("とた" vs "Tota") is good enough to *find* an
    /// item, but it must never be a reason to rename one, otherwise a library artist named
    /// とた would silently be renamed to Tota.
    /// </summary>
    /// <param name="first">First title.</param>
    /// <param name="second">Second title.</param>
    /// <returns>True when both titles are the same without romanization.</returns>
    public static bool IsSameLiteralTitle(string? first, string? second)
    {
        var left = NormalizeLiteral(first);
        var right = NormalizeLiteral(second);
        return left.Length > 0 && string.Equals(left, right, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes a title for comparison. Kana are romanized first so that a library
    /// name in kana can match the romanized name a provider returns (とた vs Tota).
    /// </summary>
    /// <param name="value">Title.</param>
    /// <returns>Lower case title without whitespace and punctuation.</returns>
    public static string Normalize(string? value)
    {
        return NormalizeLiteral(JapaneseKana.ToRomaji(value));
    }

    /// <summary>
    /// Normalizes a title without romanizing kana.
    /// </summary>
    /// <param name="value">Title.</param>
    /// <returns>Lower case title without whitespace and punctuation.</returns>
    public static string NormalizeLiteral(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSymbol(character))
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
