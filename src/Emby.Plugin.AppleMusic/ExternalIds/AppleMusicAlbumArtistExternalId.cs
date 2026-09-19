using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Emby.Plugin.AppleMusic.ExternalIds;

/// <summary>
/// Apple Music album artist external ID.
/// </summary>
public class AppleMusicAlbumArtistExternalId : IExternalId
{
    /// <inheritdoc />
    public string Name => "Apple Music Album Artist";

    /// <inheritdoc />
    public string Key => ProviderKey.AppleMusicAlbumArtist;

    /// <inheritdoc />
    public string UrlFormatString => "https://music.apple.com/artist/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is MusicArtist;
}
