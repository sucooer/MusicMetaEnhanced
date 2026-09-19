using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.AppleMusic.Dtos;
using Emby.Plugin.AppleMusic.ExternalIds;
using Emby.Plugin.AppleMusic.MetadataSources;
using Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient;
using Emby.Plugin.AppleMusic.MetadataSources.Netease;
using Emby.Plugin.AppleMusic.Utils;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.AppleMusic.Providers;

/// <summary>
/// Apple Music artist metadata provider.
/// </summary>
public class ArtistMetadataProvider : IRemoteMetadataProvider<MusicArtist, ArtistInfo>, IHasOrder, IHasSupportedExternalIdentifiers
{
    private readonly IHttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly IMetadataSource _metadataSource;
    private readonly NeteaseMusicSource _bioSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistMetadataProvider"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public ArtistMetadataProvider(IHttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _metadataSource = MetadataSourceFactory.Create(logger, httpClient);
        _bioSource = new NeteaseMusicSource(logger, new SimpleHttpClient());
    }

    /// <inheritdoc />
    public string Name => PluginUtils.PluginName;

    /// <summary>
    /// Gets the provider order. Must be smaller than the order of the other music metadata
    /// providers (TheAudioDB also uses 1) so that this provider runs first: Emby only fills
    /// fields that are still empty, so whichever provider runs first wins the biography.
    /// Running first is what makes the Chinese biography win over an English one.
    /// </summary>
    public int Order => -5;

    /// <inheritdoc />
    public string[] GetSupportedExternalIdentifiers()
    {
        return new[] { ProviderKey.AppleMusicArtist, ProviderKey.AppleMusicAlbumArtist };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(ArtistInfo searchInfo, CancellationToken cancellationToken)
    {
        var appleMusicId = searchInfo.GetProviderId(ProviderKey.AppleMusicArtist);
        if (!string.IsNullOrEmpty(appleMusicId))
        {
            _logger.Info("Apple Music: using ID {0} for artist lookup", appleMusicId);
            return await GetArtistById(appleMusicId!, cancellationToken).ConfigureAwait(false);
        }

        var searchTerm = searchInfo.Name;
        _logger.Info("Apple Music: artist ID was not provided, searching for {0}", searchTerm);

        var searchResults = await _metadataSource.SearchAsync(searchTerm, ItemType.Artist, cancellationToken).ConfigureAwait(false);
        _logger.Info("Apple Music: found {0} search results using term {1}", searchResults.Count, searchTerm);

        var allResults = new List<RemoteSearchResult>();
        foreach (var result in searchResults)
        {
            if (result is AppleMusicArtist artist)
            {
                allResults.Add(artist.ToRemoteSearchResult());
            }
        }

        return allResults;
    }

    /// <inheritdoc />
    public async Task<MetadataResult<MusicArtist>> GetMetadata(ArtistInfo info, CancellationToken cancellationToken)
    {
        var appleMusicId = info.GetProviderId(ProviderKey.AppleMusicArtist);
        var resolvedById = !string.IsNullOrEmpty(appleMusicId);
        AppleMusicArtist? artistData;

        if (resolvedById)
        {
            _logger.Debug("Apple Music: using ID {0} for artist metadata lookup", appleMusicId);
            artistData = await _metadataSource.GetArtistAsync(appleMusicId!, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Emby never lets a non MusicBrainz provider win the identify dialog, so an
            // Apple Music ID can only be missing here. Fall back to a name lookup, exactly
            // like the image provider does, and only accept an exact name match.
            artistData = await FindArtistByName(info.Name, cancellationToken).ConfigureAwait(false);
        }

        // Apple Music only ships biographies for a handful of artists, so a local
        // Netease Cloud Music API is used to fill the gap with a Chinese biography.
        var neteaseBio = await _bioSource.GetArtistBioAsync(info.Name, cancellationToken).ConfigureAwait(false);
        var overview = SelectOverview(artistData?.About, neteaseBio);

        if (artistData is null && string.IsNullOrEmpty(overview))
        {
            return EmptyMetadataResult();
        }

        var item = new MusicArtist
        {
            Overview = overview,
        };

        // Never rename the item from a fuzzy source: a stored Apple Music ID is authoritative,
        // and otherwise the name must be literally identical. Romaji equivalence (とた vs Tota)
        // is enough to find an artist, but never a reason to rename one.
        if (artistData is not null
            && (resolvedById || TitleMatcher.IsSameLiteralTitle(artistData.Name, info.Name)))
        {
            item.Name = artistData.Name;
        }

        var metadataResult = new MetadataResult<MusicArtist>
        {
            Item = item,
            HasMetadata = !string.IsNullOrEmpty(item.Name) || !string.IsNullOrEmpty(item.Overview),
        };

        if (artistData is not null && !string.IsNullOrEmpty(artistData.Id))
        {
            metadataResult.Item.SetProviderId(ProviderKey.AppleMusicArtist, artistData.Id);
        }

        return metadataResult;
    }

    /// <summary>
    /// Picks between the Apple Music and the Netease Cloud Music biography.
    /// </summary>
    /// <param name="appleBio">Apple Music biography.</param>
    /// <param name="neteaseBio">Netease Cloud Music biography.</param>
    /// <returns>Biography to store.</returns>
    private static string? SelectOverview(string? appleBio, string? neteaseBio)
    {
        if (string.IsNullOrWhiteSpace(neteaseBio))
        {
            return appleBio;
        }

        if (string.IsNullOrWhiteSpace(appleBio))
        {
            return neteaseBio;
        }

        var preferChinese = Plugin.Instance?.Configuration?.PreferNeteaseBio ?? true;
        return preferChinese ? neteaseBio : appleBio;
    }

    /// <summary>
    /// Looks an artist up by name when the item does not carry an Apple Music ID yet.
    /// </summary>
    /// <param name="name">Artist name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artist data, or null when no exact match was found.</returns>
    private async Task<AppleMusicArtist?> FindArtistByName(string name, CancellationToken cancellationToken)
    {
        _logger.Debug("Apple Music: artist ID is not available, searching for {0}", name);

        var searchResults = await _metadataSource.SearchAsync(name, ItemType.Artist, cancellationToken).ConfigureAwait(false);
        var match = searchResults.OfType<AppleMusicArtist>()
            .FirstOrDefault(a => TitleMatcher.IsSameTitle(a.Name, name));

        if (match is null)
        {
            _logger.Info("Apple Music: no artist matching '{0}' was found, skipping", name);
            return null;
        }

        if (string.IsNullOrEmpty(match.About) && !string.IsNullOrEmpty(match.Id))
        {
            var detail = await _metadataSource.GetArtistAsync(match.Id, cancellationToken).ConfigureAwait(false);
            if (detail is not null)
            {
                return detail;
            }
        }

        _logger.Info("Apple Music: matched artist '{0}' (ID {1})", match.Name, match.Id);
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

    private static MetadataResult<MusicArtist> EmptyMetadataResult()
    {
        return new MetadataResult<MusicArtist> { HasMetadata = false };
    }

    private async Task<List<RemoteSearchResult>> GetArtistById(string appleMusicId, CancellationToken cancellationToken)
    {
        var artistData = await _metadataSource.GetArtistAsync(appleMusicId, cancellationToken).ConfigureAwait(false);
        if (artistData is null)
        {
            _logger.Debug("Apple Music: no artist found for ID {0}", appleMusicId);
            return new List<RemoteSearchResult>();
        }

        return new List<RemoteSearchResult> { artistData.ToRemoteSearchResult() };
    }
}
