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
    /// Normalizes a title for comparison.
    /// </summary>
    /// <param name="value">Title.</param>
    /// <returns>Lower case title without whitespace and punctuation.</returns>
    public static string Normalize(string? value)
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
