using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models.Requests;

/// <summary>
/// Apple Music search request parameters.
/// </summary>
public class SearchRequest
{
    /// <summary>
    /// Gets or sets the search term.
    /// </summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of results.
    /// </summary>
    public int Limit { get; set; } = 25;

    /// <summary>
    /// Gets or sets the resource types to search for.
    /// </summary>
    public IEnumerable<string> Types { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Builds the query string for the request.
    /// </summary>
    /// <returns>Query string without the leading question mark.</returns>
    public string ToQueryString()
    {
        var parts = new List<string>
        {
            "term=" + Uri.EscapeDataString(Term),
            "limit=" + Limit.ToString(CultureInfo.InvariantCulture),
        };

        var types = string.Join(",", Types);
        if (!string.IsNullOrEmpty(types))
        {
            parts.Add("types=" + Uri.EscapeDataString(types));
        }

        return string.Join("&", parts.Where(p => !string.IsNullOrEmpty(p)));
    }
}
