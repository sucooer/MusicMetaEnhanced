namespace Emby.Plugin.MusicMetaEnhanced.Dtos;

/// <summary>
/// A single track of an Apple Music album.
/// </summary>
public sealed class AppleMusicTrack
{
    /// <summary>
    /// Gets or sets the Apple Music track (Adam) id. Empty when the source does not ship one.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the track title.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the track number, or null when unknown.
    /// </summary>
    public int? TrackNumber { get; set; }

    /// <summary>
    /// Gets or sets the disc number, or null when unknown.
    /// </summary>
    public int? DiscNumber { get; set; }

    /// <summary>
    /// Gets or sets the duration in milliseconds, or null when unknown.
    /// </summary>
    public int? DurationMs { get; set; }

    /// <summary>
    /// Gets or sets the track artist, when the source reports one.
    /// </summary>
    public string? ArtistName { get; set; }
}
