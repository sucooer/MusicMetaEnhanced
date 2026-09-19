using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.AppleMusic.Dtos;
using Emby.Plugin.AppleMusic.ExternalIds;
using Emby.Plugin.AppleMusic.MetadataSources;
using Emby.Plugin.AppleMusic.MetadataSources.Itunes;
using Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient;
using Emby.Plugin.AppleMusic.Utils;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.AppleMusic.Providers;

/// <summary>
/// Apple Music album metadata provider.
/// </summary>
public class AlbumMetadataProvider : IRemoteMetadataProvider<MusicAlbum, AlbumInfo>, IHasOrder, IHasSupportedExternalIdentifiers
{
    private readonly IHttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly IMetadataSource _metadataSource;
    private readonly ItunesAlbumSource _itunes;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlbumMetadataProvider"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public AlbumMetadataProvider(IHttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _metadataSource = MetadataSourceFactory.Create(logger, httpClient);
        _itunes = new ItunesAlbumSource(new SimpleHttpClient(), logger);
    }

    /// <inheritdoc />
    public string Name => PluginUtils.PluginName;

    /// <summary>
    /// Gets the provider order. Kept in front of the other music metadata providers
    /// (TheAudioDB also uses 1) so the Apple Music description wins over an English one.
    /// </summary>
    public int Order => -5;

    /// <inheritdoc />
    public string[] GetSupportedExternalIdentifiers()
    {
        return new[] { ProviderKey.AppleMusicAlbum, ProviderKey.AppleMusicAlbumArtist };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(AlbumInfo searchInfo, CancellationToken cancellationToken)
    {
        var appleMusicId = searchInfo.GetProviderId(ProviderKey.AppleMusicAlbum);
        if (!string.IsNullOrEmpty(appleMusicId))
        {
            _logger.Info("Apple Music: using ID {0} for album lookup", appleMusicId);
            return await GetAlbumById(appleMusicId!, cancellationToken).ConfigureAwait(false);
        }

        var searchTerm = searchInfo.Name;
        _logger.Info("Apple Music: album ID was not provided, searching for {0}", searchTerm);

        var searchResults = await _metadataSource.SearchAsync(searchTerm, ItemType.Album, cancellationToken).ConfigureAwait(false);
        _logger.Info("Apple Music: found {0} search results using term {1}", searchResults.Count, searchTerm);

        var allResults = new List<RemoteSearchResult>();
        foreach (var result in searchResults)
        {
            if (result is not AppleMusicAlbum album)
            {
                continue;
            }

            // Check the year only if it was specified in the search form
            if (searchInfo.Year.HasValue && searchInfo.Year != album.ReleaseDate?.Year)
            {
                continue;
            }

            allResults.Add(album.ToRemoteSearchResult());
        }

        return allResults;
    }

    /// <inheritdoc />
    public async Task<MetadataResult<MusicAlbum>> GetMetadata(AlbumInfo info, CancellationToken cancellationToken)
    {
        // When a dedicated album storefront is configured (e.g. "jp" for a Japanese
        // library), the identifying fields come from the iTunes API in that storefront's
        // own script; the web data source stays as fallback.
        var storefront = PluginUtils.ConfiguredAlbumStorefront;
        if (!string.IsNullOrEmpty(storefront))
        {
            var fromItunes = await GetMetadataFromItunes(info, storefront!, cancellationToken).ConfigureAwait(false);
            if (fromItunes is not null)
            {
                return fromItunes;
            }

            _logger.Info("Apple Music: no album data from the {0} storefront, falling back to the web source", storefront);
        }

        return await GetMetadataFromWeb(info, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the album metadata from the iTunes API in the configured storefront.
    /// </summary>
    /// <param name="info">Lookup info.</param>
    /// <param name="storefront">Storefront code, e.g. "jp".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metadata result, or null when nothing was found in that storefront.</returns>
    private async Task<MetadataResult<MusicAlbum>?> GetMetadataFromItunes(AlbumInfo info, string storefront, CancellationToken cancellationToken)
    {
        var appleMusicId = info.GetProviderId(ProviderKey.AppleMusicAlbum);
        ItunesAlbumData? data;

        if (!string.IsNullOrEmpty(appleMusicId))
        {
            data = await _itunes.LookupAsync(appleMusicId!, storefront, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            data = await FindAlbumInItunes(info, storefront, cancellationToken).ConfigureAwait(false);
        }

        if (data is null)
        {
            return null;
        }

        // The web pages ship an editorial description the iTunes API usually lacks.
        var overview = data.Description;
        if (string.IsNullOrWhiteSpace(overview))
        {
            var webData = await _metadataSource.GetAlbumAsync(data.Id, cancellationToken).ConfigureAwait(false);
            overview = webData?.About;
        }

        var item = new MusicAlbum
        {
            Overview = overview,
            ProductionYear = data.Year,
            Artists = string.IsNullOrEmpty(data.ArtistName) ? Array.Empty<string>() : new[] { data.ArtistName! },
            AlbumArtists = string.IsNullOrEmpty(data.ArtistName) ? Array.Empty<string>() : new[] { data.ArtistName! },
        };

        if (data.Genre is not null)
        {
            item.Genres = new[] { data.Genre };
        }

        var resolvedById = !string.IsNullOrEmpty(appleMusicId);

        // Same rule as the web path: a stored ID is authoritative, otherwise the title
        // must be literally identical before it may rename the item.
        if (resolvedById || TitleMatcher.IsSameLiteralTitle(data.Name, info.Name))
        {
            item.Name = data.Name;
        }

        var metadataResult = new MetadataResult<MusicAlbum>
        {
            Item = item,
            HasMetadata = !string.IsNullOrEmpty(item.Name) || !string.IsNullOrEmpty(item.Overview),
        };

        if (!string.IsNullOrEmpty(data.ArtistId))
        {
            metadataResult.Item.SetProviderId(ProviderKey.AppleMusicAlbumArtist, data.ArtistId!);
        }

        metadataResult.Item.SetProviderId(ProviderKey.AppleMusicAlbum, data.Id);
        _logger.Info("Apple Music: album '{0}' ({1}, {2}) resolved from the {3} storefront", data.Name, data.Id, data.Year, storefront);
        return metadataResult;
    }

    /// <summary>
    /// Searches the iTunes API for an album by name when the item has no stored ID yet.
    /// </summary>
    /// <param name="info">Lookup info.</param>
    /// <param name="storefront">Storefront code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album data, or null when nothing matching was found.</returns>
    private async Task<ItunesAlbumData?> FindAlbumInItunes(AlbumInfo info, string storefront, CancellationToken cancellationToken)
    {
        var albumArtist = info.AlbumArtists?.FirstOrDefault() ?? string.Empty;
        var term = string.IsNullOrEmpty(albumArtist) ? info.Name : albumArtist + " " + info.Name;

        var results = await _itunes.SearchAsync(term, storefront, cancellationToken).ConfigureAwait(false);
        var candidates = results
            .Where(a => TitleMatcher.IsSameTitle(a.Name, info.Name))
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.Info("Apple Music: the {0} storefront has no album matching '{1}'", storefront, info.Name);
            return null;
        }

        var match = info.Year.HasValue
            ? candidates.FirstOrDefault(a => a.Year == info.Year) ?? candidates[0]
            : candidates[0];

        _logger.Info("Apple Music: matched album '{0}' (ID {1}) in the {2} storefront", match.Name, match.Id, storefront);
        return await _itunes.LookupAsync(match.Id, storefront, cancellationToken).ConfigureAwait(false) ?? match;
    }

    /// <summary>
    /// The original web data path, used when no album storefront is configured or the
    /// configured one had nothing.
    /// </summary>
    /// <param name="info">Lookup info.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metadata result.</returns>
    private async Task<MetadataResult<MusicAlbum>> GetMetadataFromWeb(AlbumInfo info, CancellationToken cancellationToken)
    {
        var appleMusicId = info.GetProviderId(ProviderKey.AppleMusicAlbum);
        var resolvedById = !string.IsNullOrEmpty(appleMusicId);
        AppleMusicAlbum? albumData;

        if (!string.IsNullOrEmpty(appleMusicId))
        {
            _logger.Debug("Apple Music: using ID {0} for album metadata lookup", appleMusicId);
            albumData = await _metadataSource.GetAlbumAsync(appleMusicId!, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Emby never lets a non MusicBrainz provider win the identify dialog, so an
            // Apple Music ID can only be missing here. Fall back to a name lookup, exactly
            // like the image providers do, and only accept an exact title match.
            albumData = await FindAlbumByName(info, cancellationToken).ConfigureAwait(false);
        }

        if (albumData is null)
        {
            return EmptyMetadataResult();
        }

        var artistNames = albumData.Artists.Select(a => a.Name).ToList();

        var metadataResult = new MetadataResult<MusicAlbum>
        {
            Item = new MusicAlbum
            {
                Overview = albumData.About,
                ProductionYear = albumData.ReleaseDate?.Year,
                Artists = artistNames.ToArray(),
                AlbumArtists = artistNames.Count != 0 ? new[] { artistNames.First() } : Array.Empty<string>(),
            },
            HasMetadata = albumData.HasMetadata(),
        };

        // Never rename the item from a fuzzy source: a stored Apple Music ID is authoritative,
        // and otherwise the title must be literally identical. Romaji equivalence is enough to
        // find an album, but never a reason to rename one.
        if (resolvedById || TitleMatcher.IsSameLiteralTitle(albumData.Name, info.Name))
        {
            metadataResult.Item.Name = albumData.Name;
        }

        var albumArtist = albumData.Artists.FirstOrDefault();
        if (albumArtist is not null && !string.IsNullOrEmpty(albumArtist.Id))
        {
            metadataResult.Item.SetProviderId(ProviderKey.AppleMusicAlbumArtist, albumArtist.Id);
        }

        metadataResult.Item.SetProviderId(ProviderKey.AppleMusicAlbum, albumData.Id);
        return metadataResult;
    }

    /// <summary>
    /// Looks an album up by name when the item does not carry an Apple Music ID yet.
    /// </summary>
    /// <param name="info">Lookup info.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album data, or null when no exact match was found.</returns>
    private async Task<AppleMusicAlbum?> FindAlbumByName(AlbumInfo info, CancellationToken cancellationToken)
    {
        var albumArtist = info.AlbumArtists?.FirstOrDefault() ?? string.Empty;
        var term = string.IsNullOrEmpty(albumArtist) ? info.Name : albumArtist + " " + info.Name;

        _logger.Debug("Apple Music: album ID is not available, searching for {0}", term);

        var searchResults = await _metadataSource.SearchAsync(term, ItemType.Album, cancellationToken).ConfigureAwait(false);
        var candidates = searchResults.OfType<AppleMusicAlbum>()
            .Where(a => TitleMatcher.IsSameTitle(a.Name, info.Name))
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.Info("Apple Music: no album matching '{0}' was found, skipping", info.Name);
            return null;
        }

        // Prefer a candidate that also matches the year of the item.
        var match = info.Year.HasValue
            ? candidates.FirstOrDefault(a => a.ReleaseDate?.Year == info.Year) ?? candidates[0]
            : candidates[0];

        if (string.IsNullOrEmpty(match.About) && !string.IsNullOrEmpty(match.Id))
        {
            var detail = await _metadataSource.GetAlbumAsync(match.Id, cancellationToken).ConfigureAwait(false);
            if (detail is not null)
            {
                return detail;
            }
        }

        _logger.Info("Apple Music: matched album '{0}' (ID {1})", match.Name, match.Id);
        return match;
    }

    /// <inheritdoc />
    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClient.GetResponse(new HttpRequestOptions
        {
            Url = url,
            CancellationToken = cancellationToken,
        });
    }

    private static MetadataResult<MusicAlbum> EmptyMetadataResult()
    {
        return new MetadataResult<MusicAlbum> { HasMetadata = false };
    }

    private async Task<List<RemoteSearchResult>> GetAlbumById(string appleMusicId, CancellationToken cancellationToken)
    {
        var albumData = await _metadataSource.GetAlbumAsync(appleMusicId, cancellationToken).ConfigureAwait(false);
        if (albumData is null)
        {
            _logger.Debug("Apple Music: no album found for ID {0}", appleMusicId);
            return new List<RemoteSearchResult>();
        }

        return new List<RemoteSearchResult> { albumData.ToRemoteSearchResult() };
    }
}
