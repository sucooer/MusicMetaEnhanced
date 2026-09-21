namespace Emby.Plugin.MusicMetaEnhanced.ExternalIds;

/// <summary>
/// Apple Music provider keys. These are the keys used in the Emby provider ID dictionary.
/// </summary>
public static class ProviderKey
{
    /// <summary>
    /// Apple Music album provider ID.
    /// </summary>
    public const string AppleMusicAlbum = "AppleMusicAlbum";

    /// <summary>
    /// Apple Music album artist provider ID.
    /// </summary>
    public const string AppleMusicAlbumArtist = "AppleMusicAlbumArtist";

    /// <summary>
    /// Apple Music artist provider ID.
    /// </summary>
    public const string AppleMusicArtist = "AppleMusicArtist";

    /// <summary>
    /// Apple Music song provider ID. Songs live inside an album, so this is the
    /// track Adam id, not the album one.
    /// </summary>
    public const string AppleMusicSong = "AppleMusicSong";
}
