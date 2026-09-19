using System.Text.Json.Serialization;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models.Responses;

/// <summary>
/// Apple Music search response.
/// </summary>
public class SearchResponse
{
    /// <summary>
    /// Gets or sets the album results.
    /// </summary>
    [JsonPropertyName("albums")]
    public ResourceList? Albums { get; set; }

    /// <summary>
    /// Gets or sets the artist results.
    /// </summary>
    [JsonPropertyName("artists")]
    public ResourceList? Artists { get; set; }
}
