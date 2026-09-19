using System.Text.Json.Serialization;

namespace Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient.Models;

/// <summary>
/// Apple Music artwork.
/// </summary>
public class Artwork
{
    /// <summary>
    /// Gets or sets the artwork URL template using {w}, {h} and {f} placeholders.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the artwork width.
    /// </summary>
    [JsonPropertyName("width")]
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets the artwork height.
    /// </summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }
}
