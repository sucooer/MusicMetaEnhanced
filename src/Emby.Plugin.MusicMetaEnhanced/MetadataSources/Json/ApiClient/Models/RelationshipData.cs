using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models;

/// <summary>
/// Apple Music relationship data container.
/// </summary>
public class RelationshipData
{
    /// <summary>
    /// Gets or sets the related resources.
    /// </summary>
    [JsonPropertyName("data")]
    public List<SearchResult>? Data { get; set; }
}
