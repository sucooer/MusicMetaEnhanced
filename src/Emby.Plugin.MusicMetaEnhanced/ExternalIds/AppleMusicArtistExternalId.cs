using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Emby.Plugin.MusicMetaEnhanced.ExternalIds;

/// <summary>
/// Apple Music artist external ID.
/// </summary>
public class AppleMusicArtistExternalId : IExternalId
{
    /// <inheritdoc />
    public string Name => "Apple Music Artist";

    /// <inheritdoc />
    public string Key => ProviderKey.AppleMusicArtist;

    /// <inheritdoc />
    public string UrlFormatString => "https://music.apple.com/artist/{0}";

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is MusicArtist;
}
