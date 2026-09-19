using System.Text.Json.Serialization;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models;

/// <summary>
/// Apple Music editorial notes.
/// </summary>
public class EditorialNotes
{
    /// <summary>
    /// Gets or sets the short note.
    /// </summary>
    [JsonPropertyName("short")]
    public string? Short { get; set; }

    /// <summary>
    /// Gets or sets the standard note.
    /// </summary>
    [JsonPropertyName("standard")]
    public string? Standard { get; set; }
}
