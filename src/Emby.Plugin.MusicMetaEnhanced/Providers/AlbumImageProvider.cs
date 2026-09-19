using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MusicMetaEnhanced.Dtos;
using Emby.Plugin.MusicMetaEnhanced.ExternalIds;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources;
using Emby.Plugin.MusicMetaEnhanced.Utils;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.MusicMetaEnhanced.Providers;

/// <summary>
/// Apple Music album image provider.
/// </summary>
public class AlbumImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly IHttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly IMetadataSource _metadataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlbumImageProvider"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public AlbumImageProvider(IHttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _metadataSource = MetadataSourceFactory.Create(logger, httpClient);
    }

    /// <inheritdoc />
    public string Name => PluginUtils.PluginName;

    /// <inheritdoc />
    public int Order => 1;

    /// <inheritdoc />
    public bool Supports(BaseItem item) => item is MusicAlbum;

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new List<ImageType> { ImageType.Primary };
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions, CancellationToken cancellationToken)
    {
        if (item is not MusicAlbum album)
        {
            _logger.Debug("Apple Music: provided item is not an album, cannot continue");
            return new List<RemoteImageInfo>();
        }

        var appleMusicId = album.GetProviderId(ProviderKey.AppleMusicAlbum);
        if (!string.IsNullOrEmpty(appleMusicId))
        {
            _logger.Info("Apple Music: using ID {0} for album image lookup", appleMusicId);
            return await GetImageById(appleMusicId!, cancellationToken).ConfigureAwait(false);
        }

        var term = GetSearchTerm(album);
        _logger.Info("Apple Music: album ID is not available, using search with term {0}", term);

        var searchResults = await _metadataSource.SearchAsync(term, ItemType.Album, cancellationToken).ConfigureAwait(false);
        _logger.Info("Apple Music: found {0} search results using term {1}", searchResults.Count, term);

        // The search term is "<artist> <album>", so Apple Music returns loosely related
        // releases too. Only a release with the same title may be used, and since Apple
        // sorts by relevance, the first match is the best one.
        var match = searchResults
            .OfType<AppleMusicAlbum>()
            .FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate.ImageUrl)
                                         && TitleMatcher.IsSameTitle(candidate.Name, album.Name));

        if (match is null)
        {
            _logger.Info("Apple Music: no album named '{0}' was found, no images to offer", album.Name);
            return new List<RemoteImageInfo>();
        }

        _logger.Info("Apple Music: matched album '{0}' (ID {1})", match.Name, match.Id);
        return new List<RemoteImageInfo> { CreateImageInfo(match.ImageUrl!) };
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

    private static string GetSearchTerm(MusicAlbum album)
    {
        var albumArtist = album.AlbumArtists.FirstOrDefault() ?? string.Empty;
        return $"{albumArtist} {album.Name}";
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
        var albumData = await _metadataSource.GetAlbumAsync(appleMusicId, cancellationToken).ConfigureAwait(false);
        if (albumData?.ImageUrl is null)
        {
            _logger.Debug("Apple Music: could not find image for album ID {0}", appleMusicId);
            return new List<RemoteImageInfo>();
        }

        return new List<RemoteImageInfo> { CreateImageInfo(albumData.ImageUrl) };
    }
}
