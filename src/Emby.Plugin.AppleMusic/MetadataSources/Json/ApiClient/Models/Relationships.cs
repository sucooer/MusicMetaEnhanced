using System.Text.Json.Serialization;

namespace Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient.Models;

/// <summary>
/// Apple Music resource relationships.
/// </summary>
public class Relationships
{
    /// <summary>
    /// Gets or sets the artists relationship.
    /// </summary>
    [JsonPropertyName("artists")]
    public RelationshipData? Artists { get; set; }
}
