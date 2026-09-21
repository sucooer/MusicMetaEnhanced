using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Emby.Plugin.MusicMetaEnhanced.ExternalIds;

/// <summary>
/// Apple Music song external ID.
/// Emby has no identify dialog for songs, so this is only a manual entry point:
/// pasting a track Adam id lets the song provider resolve the song directly
/// instead of going through its album.
/// </summary>
public class AppleMusicSongExternalId : IExternalId
{
    /// <inheritdoc />
    public string Name => "Apple Music Song";

    /// <inheritdoc />
    public string Key => ProviderKey.AppleMusicSong;

    /// <inheritdoc />
    public string UrlFormatString => "https://music.apple.com/song/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Audio;
}
