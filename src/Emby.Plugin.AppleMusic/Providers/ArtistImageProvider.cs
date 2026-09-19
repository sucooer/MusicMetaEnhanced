using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.AppleMusic.Dtos;
using Emby.Plugin.AppleMusic.ExternalIds;
using Emby.Plugin.AppleMusic.MetadataSources;
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
            return await GetImageById(appleMusicId!, cancellationToken).ConfigureAwait(false);
        }

        _logger.Info("Apple Music: artist ID is not available, using search with artist name {0}", artist.Name);

        var searchResults = await _metadataSource.SearchAsync(artist.Name, ItemType.Artist, cancellationToken).ConfigureAwait(false);
        _logger.Info("Apple Music: found {0} search results using term {1}", searchResults.Count, artist.Name);

        var images = new List<RemoteImageInfo>();
        foreach (var result in searchResults)
        {
            if (result is AppleMusicArtist amArtist && !string.IsNullOrEmpty(amArtist.ImageUrl))
            {
                images.Add(CreateImageInfo(amArtist.ImageUrl!));
            }
        }

        return images;
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
