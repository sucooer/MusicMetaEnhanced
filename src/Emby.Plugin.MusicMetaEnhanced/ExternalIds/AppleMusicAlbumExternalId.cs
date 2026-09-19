using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Emby.Plugin.MusicMetaEnhanced.ExternalIds;

/// <summary>
/// Apple Music album external ID.
/// </summary>
public class AppleMusicAlbumExternalId : IExternalId
{
    /// <inheritdoc />
    public string Name => "Apple Music Album";

    /// <inheritdoc />
    public string Key => ProviderKey.AppleMusicAlbum;

    /// <inheritdoc />
    public string UrlFormatString => "https://music.apple.com/album/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is MusicAlbum;
}
