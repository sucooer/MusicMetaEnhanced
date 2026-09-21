using System;
using System.Collections.Generic;
using System.Linq;
using Emby.Plugin.MusicMetaEnhanced.ExternalIds;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.MusicMetaEnhanced.Dtos;

/// <summary>
/// Apple Music album item.
/// </summary>
public class AppleMusicAlbum : IAppleMusicItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AppleMusicAlbum"/> class.
    /// </summary>
    public AppleMusicAlbum()
    {
        Id = string.Empty;
        Name = string.Empty;
        Url = string.Empty;
        Artists = new List<AppleMusicArtist>();
        Tracks = new List<AppleMusicTrack>();
    }

    /// <inheritdoc />
    public string Id { get; set; }

    /// <inheritdoc />
    public string Name { get; set; }

    /// <inheritdoc />
    public string Url { get; set; }

    /// <inheritdoc />
    public string? ImageUrl { get; set; }

    /// <inheritdoc />
    public string? About { get; set; }

    /// <summary>
    /// Gets or sets the artists.
    /// The first artist should also be the album artist.
    /// </summary>
    public IEnumerable<AppleMusicArtist> Artists { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets the track list. Only the album detail page carries it; search
    /// results never do, so an album resolved from a search has to be fetched by id
    /// before its tracks are known.
    /// </summary>
    public IList<AppleMusicTrack> Tracks { get; set; }

    /// <inheritdoc />
    public RemoteSearchResult ToRemoteSearchResult()
    {
        var artists = Artists.ToList();

        return new RemoteSearchResult
        {
            Name = Name,
            ImageUrl = ImageUrl,
            Overview = About,
            PremiereDate = ReleaseDate.HasValue ? new DateTimeOffset(ReleaseDate.Value) : null,
            ProductionYear = ReleaseDate?.Year,
            AlbumArtist = artists.FirstOrDefault()?.ToRemoteSearchAlbumArtistResult(),
            Artists = artists.Select(a => a.ToRemoteSearchResult()).ToArray(),
            ProviderIds = new ProviderIdDictionary { { ProviderKey.AppleMusicAlbum, Id } },
        };
    }

    /// <inheritdoc />
    public bool HasMetadata()
    {
        return !string.IsNullOrEmpty(Name) || About is not null || ReleaseDate is not null;
    }
}
