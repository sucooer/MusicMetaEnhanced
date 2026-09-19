using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient.Models;

/// <summary>
/// A single resource returned by the Apple Music API.
/// </summary>
public class SearchResult
{
    /// <summary>
    /// Gets or sets the resource ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource type (albums, artists, ...).
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resource attributes.
    /// </summary>
    [JsonPropertyName("attributes")]
    public Attributes? Attributes { get; set; }

    /// <summary>
    /// Gets or sets the resource relationships.
    /// </summary>
    [JsonPropertyName("relationships")]
    public Relationships? Relationships { get; set; }
}
