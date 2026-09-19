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
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.AppleMusic.Providers;

/// <summary>
/// Apple Music artist image provider.
/// </summary>
public class ArtistImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly IMetadataSource _metadataSource;
    private readonly NeteaseMusicSource _neteaseSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistImageProvider"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public ArtistImageProvider(IHttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _metadataSource = MetadataSourceFactory.Create(logger, httpClient);
        _neteaseSource = new NeteaseMusicSource(logger, new SimpleHttpClient());
    }

    /// <inheritdoc />
    public string Name => PluginUtils.PluginName;

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicArtist;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new List<ImageType> { ImageType.Primary };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
    {
        if (item is not MusicArtist artist)
        {
            _logger.Debug("Apple Music: provided item is not an artist, cannot continue");
            return new List<RemoteImageInfo>();
        }

        var appleMusicId = artist.GetProviderId(ProviderKey.AppleMusicArtist)
                           ?? artist.GetProviderId(ProviderKey.AppleMusicAlbumArtist);

        if (!string.IsNullOrEmpty(appleMusicId))
        {
            _logger.Info("Apple Music: using ID {0} for artist image lookup", appleMusicId);
            var byId = await GetImageById(appleMusicId!, cancellationToken).ConfigureAwait(false);
            if (byId.Count > 0)
            {
                return byId;
            }

            // A stored ID can point at an artist Apple Music itself has no image for (the
            // library artist 瑞葵 carries such an ID), so do not stop here - fall through to
            // the name lookup, and from there to the Netease fallback.
            _logger.Info("Apple Music: artist ID {0} has no image, falling back to a name lookup", appleMusicId);
        }

        _logger.Info("Apple Music: looking up the image of '{0}' by name", artist.Name);

        var searchResults = await _metadataSource.SearchAsync(artist.Name, ItemType.Artist, cancellationToken).ConfigureAwait(false);
        _logger.Info("Apple Music: found {0} search results using term {1}", searchResults.Count, artist.Name);

        // Apple Music's artist search is fuzzy - searching とた also returns Pete Townshend and
        // Pat Benatar - so only a result with the same name may be used. A parenthesised suffix
        // is tolerated ("fripSide(vocal:Mao Uesugi)" for the library's fripSide), but only as a
        // second attempt. Apple sorts by relevance, so the first match is the best one; later
        // matches are just different artists sharing the name (there are two artists called
        // "Tota"), and offering them would only make the picker confusing.
        var match = searchResults
            .OfType<AppleMusicArtist>()
            .FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate.ImageUrl)
                                         && TitleMatcher.IsSameTitle(candidate.Name, artist.Name))
            ?? searchResults
                .OfType<AppleMusicArtist>()
                .FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate.ImageUrl)
                                             && TitleMatcher.IsSameNameIgnoringSuffix(candidate.Name, artist.Name));

        if (match is null)
        {
            // Apple Music localizes some artist names (花澤香菜 becomes 花泽香菜), and those can
            // never match the library name. Netease keeps the original name, so it is used as a
            // fallback image source for exactly those artists.
            var neteaseImage = await _neteaseSource.GetArtistImageUrlAsync(artist.Name, cancellationToken).ConfigureAwait(false);

            if (neteaseImage is null)
            {
                _logger.Info("Apple Music: no artist named '{0}' was found, no images to offer", artist.Name);
                return new List<RemoteImageInfo>();
            }

            return new List<RemoteImageInfo> { CreateNeteaseImageInfo(neteaseImage) };
        }

        _logger.Info("Apple Music: matched artist '{0}' (ID {1})", match.Name, match.Id);
        return new List<RemoteImageInfo> { CreateImageInfo(match.ImageUrl!) };
    }

    private RemoteImageInfo CreateNeteaseImageInfo(string imageUrl)
    {
        return new RemoteImageInfo
        {
            Height = 1400,
            Width = 1400,
            ProviderName = Name,
            ThumbnailUrl = NeteaseMusicSource.WithSize(imageUrl, 100),
            Type = ImageType.Primary,
            Url = imageUrl,
        };
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

    private RemoteImageInfo CreateImageInfo(string imageUrl)
    {
        return new RemoteImageInfo
        {
            Height = 1400,
            Width = 1400,
            ProviderName = Name,
            ThumbnailUrl = PluginUtils.UpdateImageSize(imageUrl, "100x100cc"),
            Type = ImageType.Primary,
            Url = PluginUtils.UpdateImageSize(imageUrl, "1400x1400cc"),
        };
    }

    private async Task<List<RemoteImageInfo>> GetImageById(string appleMusicId, CancellationToken cancellationToken)
    {
        var artistData = await _metadataSource.GetArtistAsync(appleMusicId, cancellationToken).ConfigureAwait(false);
        if (artistData?.ImageUrl is null)
        {
            _logger.Debug("Apple Music: could not find image for artist ID {0}", appleMusicId);
            return new List<RemoteImageInfo>();
        }

        return new List<RemoteImageInfo> { CreateImageInfo(artistData.ImageUrl) };
    }
}
