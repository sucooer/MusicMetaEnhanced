using System.Collections.Generic;
using System.Text.Json.Serialization;
using Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient.Models;

namespace Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient.Models.Responses;

/// <summary>
/// Apple Music single resource response.
/// </summary>
public class ResourceResponse
{
    /// <summary>
    /// Gets or sets the resources.
    /// </summary>
    [JsonPropertyName("data")]
    public List<SearchResult> Data { get; set; } = new();
}
