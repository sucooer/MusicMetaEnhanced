using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Web;

/// <summary>
/// Extracts the structured page data that Apple Music embeds into its web pages.
/// Apple Music is a SvelteKit application and ships the whole page model inside a
/// script tag with the id "serialized-server-data". Reading that JSON is far more
/// stable than scraping the rendered DOM, and - unlike the DOM - it is not affected
/// by the language of the storefront.
/// </summary>
public static class AppleMusicPageParser
{
    private const string ScriptId = "serialized-server-data";

    /// <summary>
    /// Extracts the embedded page JSON from an Apple Music page.
    /// </summary>
    /// <param name="html">Page HTML.</param>
    /// <returns>Parsed JSON document, or null when the page does not contain the data.</returns>
    public static JsonDocument? Parse(string html)
    {
        var start = html.IndexOf(ScriptId, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var open = html.IndexOf('>', start);
        if (open < 0)
        {
            return null;
        }

        var end = html.IndexOf("</script>", open, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        var json = html.Substring(open + 1, end - open - 1).Trim();
        if (json.Length == 0)
        {
            return null;
        }

        return JsonDocument.Parse(json);
    }

    /// <summary>
    /// Gets the "sections" array of the page data.
    /// </summary>
    /// <param name="document">Parsed page data.</param>
    /// <returns>List of section elements, empty when not found.</returns>
    public static List<JsonElement> GetSections(JsonDocument document)
    {
        var result = new List<JsonElement>();

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("data", out var inner)
                && inner.TryGetProperty("sections", out var sections)
                && sections.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sections.EnumerateArray())
                {
                    result.Add(section);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the items of a section.
    /// </summary>
    /// <param name="section">Section element.</param>
    /// <returns>List of item elements, empty when not found.</returns>
    public static List<JsonElement> GetItems(JsonElement section)
    {
        var result = new List<JsonElement>();

        if (section.ValueKind != JsonValueKind.Object
            || !section.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            result.Add(item);
        }

        return result;
    }

    /// <summary>
    /// Gets a string property, returning null when missing or not a string.
    /// </summary>
    /// <param name="element">JSON element.</param>
    /// <param name="name">Property name.</param>
    /// <returns>Property value.</returns>
    public static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    /// <summary>
    /// Gets an integer property, returning null when missing or not a number.
    /// </summary>
    /// <param name="element">JSON element.</param>
    /// <param name="name">Property name.</param>
    /// <returns>Property value.</returns>
    public static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    }

    /// <summary>
    /// Gets a nested string property using a path of property names.
    /// </summary>
    /// <param name="element">JSON element.</param>
    /// <param name="path">Property names.</param>
    /// <returns>Property value.</returns>
    public static string? GetStringPath(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next))
            {
                return null;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    /// <summary>
    /// Gets an identifier that may be encoded as a JSON string or as a number.
    /// </summary>
    /// <param name="element">JSON element.</param>
    /// <param name="path">Property names.</param>
    /// <returns>Identifier as string, or null when not found.</returns>
    public static string? GetIdPath(JsonElement element, params string[] path)
    {
        var current = GetPath(element, path);
        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            _ => null,
        };
    }

    /// <summary>
    /// Gets a nested element using a path of property names.
    /// </summary>
    /// <param name="element">JSON element.</param>
    /// <param name="path">Property names.</param>
    /// <returns>Element, or an undefined element when the path does not exist.</returns>
    public static JsonElement GetPath(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next))
            {
                return default;
            }

            current = next;
        }

        return current;
    }
}
