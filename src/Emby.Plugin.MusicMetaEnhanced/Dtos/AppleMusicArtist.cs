using System.Collections.Generic;
using Emby.Plugin.MusicMetaEnhanced.ExternalIds;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.MusicMetaEnhanced.Dtos;

/// <summary>
/// Apple Music artist item.
/// </summary>
public class AppleMusicArtist : IAppleMusicItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppleMusicArtist"/> class.
    /// </summary>
    public AppleMusicArtist()
    {
        Id = string.Empty;
        Name = string.Empty;
        Url = string.Empty;
    }

    /// <inheritdoc />
    public string Id { get; set; }

    /// <inheritdoc />
    public string Name { get; set; }

    /// <inheritdoc />
    public string Url { get; set; }

    /// <inheritdoc />
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the wide (2:1) artwork URL. Apple Music ships this for some artists only
    /// and it is the only genuinely landscape image the service has, so it backs backdrops.
    /// </summary>
    public string? WideImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the artist logo URL (a transparent PNG when Apple Music has one).
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <inheritdoc />
    public string? About { get; set; }

    /// <inheritdoc />
    public RemoteSearchResult ToRemoteSearchResult()
    {
        return new RemoteSearchResult
        {
            Name = Name,
            ImageUrl = ImageUrl,
            Overview = About,
            ProviderIds = new ProviderIdDictionary { { ProviderKey.AppleMusicArtist, Id } },
        };
    }

    /// <summary>
    /// Converts the artist to an album artist remote search result.
    /// </summary>
    /// <returns>Remote search result carrying the album artist provider ID.</returns>
    public RemoteSearchResult ToRemoteSearchAlbumArtistResult()
    {
        return new RemoteSearchResult
        {
            Name = Name,
            ImageUrl = ImageUrl,
            Overview = About,
            ProviderIds = new ProviderIdDictionary { { ProviderKey.AppleMusicAlbumArtist, Id } },
        };
    }

    /// <inheritdoc />
    public bool HasMetadata()
    {
        return !string.IsNullOrEmpty(Name) || About is not null;
    }
}
