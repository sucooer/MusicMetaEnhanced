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
    /// Gets a value indicating whether two names refer to the same entity when a
    /// parenthesised suffix is ignored. Providers bolt qualifiers onto names that the
    /// library does not have - Netease stores "瑞葵(mizuki)" for the artist the library
    /// knows as "瑞葵", and Apple Music has "fripSide(vocal:Mao Uesugi)" for "fripSide".
    /// This is a *second* attempt only: callers use it after the exact comparison failed.
    /// </summary>
    /// <param name="first">First name.</param>
    /// <param name="second">Second name.</param>
    /// <returns>True when both names match once the parenthesised part is dropped.</returns>
    public static bool IsSameNameIgnoringSuffix(string? first, string? second)
    {
        if (IsSameTitle(first, second))
        {
            return true;
        }

        var left = Normalize(StripParenthetical(first));
        var right = Normalize(StripParenthetical(second));
        return left.Length > 0 && string.Equals(left, right, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets a value indicating whether a provider's name is the library name plus a
    /// parenthesised qualifier - the provider says "瑞葵(mizuki)" where the library has plain
    /// "瑞葵". This is the *safe* half of the suffix rule: the provider only adds detail to the
    /// same name, so its data does describe this item.
    /// The opposite case is rejected on purpose: when the *library* name carries the qualifier
    /// ("高木さん(CV:高橋李依)") the library item is a narrower entity, and data filed under the
    /// plain name may not describe it.
    /// </summary>
    /// <param name="providerName">Name as returned by the provider.</param>
    /// <param name="libraryName">Name stored in the library.</param>
    /// <returns>True when the provider name matches and only adds a qualifier.</returns>
    public static bool IsProviderNameQualified(string? providerName, string? libraryName)
    {
        if (string.IsNullOrWhiteSpace(providerName) || string.IsNullOrWhiteSpace(libraryName))
        {
            return false;
        }

        var stripped = StripParenthetical(providerName);
        if (string.Equals(stripped, providerName, System.StringComparison.Ordinal))
        {
            // Nothing to strip, so the exact comparison already had its chance.
            return false;
        }

        return IsSameTitle(stripped, libraryName);
    }

    /// <summary>
    /// Drops the first parenthesised part of a name, keeping the part before it.
    /// Handles both half width and full width brackets.
    /// </summary>
    /// <param name="value">Name.</param>
    /// <returns>Name without its parenthesised suffix.</returns>
    private static string? StripParenthetical(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var cut = value.Length;
        foreach (var opening in new[] { '(', '（', '[', '【' })
        {
            var index = value.IndexOf(opening);
            if (index > 0 && index < cut)
            {
                cut = index;
            }
        }

        return cut < value.Length ? value[..cut] : value;
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
