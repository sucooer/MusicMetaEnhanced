using System;
using System.Collections.Generic;
using System.Text;

namespace Emby.Plugin.AppleMusic.Utils;

/// <summary>
/// Converts Japanese kana to romaji so that a library name written in kana can be
/// compared with the romanized name Apple Music returns (for example the library
/// artist "とた" against Apple Music's "Tota").
/// A Hepburn-like table is used; it only has to be good enough for name comparison,
/// so kanji are passed through untouched.
/// </summary>
public static class JapaneseKana
{
    /// <summary>
    /// Longest-first kana table, so that digraphs such as きゃ are matched before き.
    /// </summary>
    private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
    {
        // Digraphs (拗音).
        ["きゃ"] = "kya", ["きゅ"] = "kyu", ["きょ"] = "kyo",
        ["ぎゃ"] = "gya", ["ぎゅ"] = "gyu", ["ぎょ"] = "gyo",
        ["しゃ"] = "sha", ["しゅ"] = "shu", ["しょ"] = "sho",
        ["じゃ"] = "ja", ["じゅ"] = "ju", ["じょ"] = "jo",
        ["ちゃ"] = "cha", ["ちゅ"] = "chu", ["ちょ"] = "cho",
        ["ぢゃ"] = "ja", ["ぢゅ"] = "ju", ["ぢょ"] = "jo",
        ["にゃ"] = "nya", ["にゅ"] = "nyu", ["にょ"] = "nyo",
        ["ひゃ"] = "hya", ["ひゅ"] = "hyu", ["ひょ"] = "hyo",
        ["びゃ"] = "bya", ["びゅ"] = "byu", ["びょ"] = "byo",
        ["ぴゃ"] = "pya", ["ぴゅ"] = "pyu", ["ぴょ"] = "pyo",
        ["みゃ"] = "mya", ["みゅ"] = "myu", ["みょ"] = "myo",
        ["りゃ"] = "rya", ["りゅ"] = "ryu", ["りょ"] = "ryo",

        // Foreign sound combinations (2 kana).
        ["いぇ"] = "ye",
        ["うぃ"] = "wi", ["うぇ"] = "we", ["うぉ"] = "wo",
        ["しぇ"] = "she", ["じぇ"] = "je", ["ちぇ"] = "che",
        ["てぃ"] = "ti", ["でぃ"] = "di", ["とぅ"] = "tu", ["どぅ"] = "du",
        ["ふぁ"] = "fa", ["ふぃ"] = "fi", ["ふぇ"] = "fe", ["ふぉ"] = "fo",
        ["ゔぁ"] = "va", ["ゔぃ"] = "vi", ["ゔぇ"] = "ve", ["ゔぉ"] = "vo",

        // Base syllables.
        ["あ"] = "a", ["い"] = "i", ["う"] = "u", ["え"] = "e", ["お"] = "o",
        ["か"] = "ka", ["き"] = "ki", ["く"] = "ku", ["け"] = "ke", ["こ"] = "ko",
        ["が"] = "ga", ["ぎ"] = "gi", ["ぐ"] = "gu", ["げ"] = "ge", ["ご"] = "go",
        ["さ"] = "sa", ["し"] = "shi", ["す"] = "su", ["せ"] = "se", ["そ"] = "so",
        ["ざ"] = "za", ["じ"] = "ji", ["ず"] = "zu", ["ぜ"] = "ze", ["ぞ"] = "zo",
        ["た"] = "ta", ["ち"] = "chi", ["つ"] = "tsu", ["て"] = "te", ["と"] = "to",
        ["だ"] = "da", ["ぢ"] = "ji", ["づ"] = "zu", ["で"] = "de", ["ど"] = "do",
        ["な"] = "na", ["に"] = "ni", ["ぬ"] = "nu", ["ね"] = "ne", ["の"] = "no",
        ["は"] = "ha", ["ひ"] = "hi", ["ふ"] = "fu", ["へ"] = "he", ["ほ"] = "ho",
        ["ば"] = "ba", ["び"] = "bi", ["ぶ"] = "bu", ["べ"] = "be", ["ぼ"] = "bo",
        ["ぱ"] = "pa", ["ぴ"] = "pi", ["ぷ"] = "pu", ["ぺ"] = "pe", ["ぽ"] = "po",
        ["ま"] = "ma", ["み"] = "mi", ["む"] = "mu", ["め"] = "me", ["も"] = "mo",
        ["や"] = "ya", ["ゆ"] = "yu", ["よ"] = "yo",
        ["ら"] = "ra", ["り"] = "ri", ["る"] = "ru", ["れ"] = "re", ["ろ"] = "ro",
        ["わ"] = "wa", ["ゐ"] = "i", ["ゑ"] = "e", ["を"] = "o", ["ん"] = "n",
        ["ゔ"] = "vu",

        // Small kana standing alone.
        ["ぁ"] = "a", ["ぃ"] = "i", ["ぅ"] = "u", ["ぇ"] = "e", ["ぉ"] = "o",
        ["ゃ"] = "ya", ["ゅ"] = "yu", ["ょ"] = "yo", ["ゎ"] = "wa",
    };

    /// <summary>
    /// Converts every kana in the value to romaji, leaving everything else untouched.
    /// </summary>
    /// <param name="value">Value that may contain kana.</param>
    /// <returns>Value with kana replaced by lower case romaji.</returns>
    public static string ToRomaji(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var hiragana = ToHiragana(value);
        var builder = new StringBuilder(hiragana.Length * 2);
        var index = 0;

        while (index < hiragana.Length)
        {
            var current = hiragana[index];

            // ー and － only lengthen the previous vowel, which adds no consonant
            // information that a name comparison would need.
            if (current is 'ー' or '－')
            {
                index++;
                continue;
            }

            if (current is 'っ')
            {
                // Sokuon: the following consonant is doubled.
                if (TryLookupLongest(hiragana, index + 1, out _, out var following)
                    && following.Length > 0
                    && !IsVowel(following[0]))
                {
                    builder.Append(following[0]);
                }

                index++;
                continue;
            }

            if (TryLookupLongest(hiragana, index, out var length, out var romaji))
            {
                builder.Append(romaji);
                index += length;
                continue;
            }

            // Not kana (kanji, latin, digits, ...) - keep it as it is.
            builder.Append(current);
            index++;
        }

        return builder.ToString();
    }

    private static bool IsVowel(char character) => character is 'a' or 'i' or 'u' or 'e' or 'o';

    /// <summary>
    /// Converts katakana to hiragana so a single table can serve both scripts.
    /// </summary>
    private static string ToHiragana(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            // Katakana letters live 0x60 above their hiragana counterparts.
            if (character >= '\u30a1' && character <= '\u30f6')
            {
                builder.Append((char)(character - 0x60));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool TryLookupLongest(string value, int start, out int length, out string romaji)
    {
        for (var size = 3; size >= 1; size--)
        {
            if (start + size > value.Length)
            {
                continue;
            }

            if (Table.TryGetValue(value.Substring(start, size), out var found))
            {
                length = size;
                romaji = found;
                return true;
            }
        }

        length = 0;
        romaji = string.Empty;
        return false;
    }
}
