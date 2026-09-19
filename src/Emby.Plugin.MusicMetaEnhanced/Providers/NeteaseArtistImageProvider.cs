using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Netease;
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
/// Artist image provider backed by Netease Cloud Music.
/// It is a separate provider on purpose: Emby's "source" dropdown lists image providers, so a
/// second source only shows up there when it is one. Netease also keeps the artist's original
/// name, which covers the artists Apple Music localizes away from the library name
/// (花澤香菜 -> 花泽香菜) and the ones Apple Music has no image for at all.
/// </summary>
public class NeteaseArtistImageProvider : IRemoteImageProvider, IHasOrder
{
    /// <summary>
    /// Image size requested from Netease, matching the Apple Music provider.
    /// </summary>
    private const int ImageSize = 1400;

    private readonly IHttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly NeteaseMusicSource _source;

    /// <summary>
    /// Initializes a new instance of the <see cref="NeteaseArtistImageProvider"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public NeteaseArtistImageProvider(IHttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _source = new NeteaseMusicSource(logger, new SimpleHttpClient());
    }

    /// <inheritdoc />
    public string Name => "网易云音乐";

    /// <inheritdoc />
    public int Order => 2;

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
            _logger.Debug("Netease: provided item is not an artist, cannot continue");
            return new List<RemoteImageInfo>();
        }

        var imageUrl = await _source.GetArtistImageUrlAsync(artist.Name, cancellationToken).ConfigureAwait(false);
        if (imageUrl is null)
        {
            return new List<RemoteImageInfo>();
        }

        return new List<RemoteImageInfo>
        {
            new RemoteImageInfo
            {
                Height = ImageSize,
                Width = ImageSize,
                ProviderName = Name,
                ThumbnailUrl = NeteaseMusicSource.WithSize(imageUrl, 100),
                Type = ImageType.Primary,
                Url = imageUrl,
            },
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
}
