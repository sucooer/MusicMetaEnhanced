using System.Collections.Generic;
using System.Text.Json.Serialization;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models.Responses;

/// <summary>
/// A list of Apple Music resources.
/// </summary>
public class ResourceList
{
    /// <summary>
    /// Gets or sets the resources.
    /// </summary>
    [JsonPropertyName("data")]
    public List<SearchResult> Data { get; set; } = new();
}
