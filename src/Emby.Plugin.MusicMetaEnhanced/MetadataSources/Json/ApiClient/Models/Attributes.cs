using System.Text.Json.Serialization;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models;

/// <summary>
/// Common Apple Music resource attributes.
/// </summary>
public class Attributes
{
    /// <summary>
    /// Gets or sets the resource name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Apple Music web URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the artwork.
    /// </summary>
    [JsonPropertyName("artwork")]
    public Artwork? Artwork { get; set; }

    /// <summary>
    /// Gets or sets the editorial notes (description).
    /// </summary>
    [JsonPropertyName("editorialNotes")]
    public EditorialNotes? EditorialNotes { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }
}
