using System.Collections.Generic;
using System.Linq;
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
        return new List<ImageType> { ImageType.Primary, ImageType.Backdrop, ImageType.Logo };
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

        var detail = await FindArtistAsync(artist, appleMusicId, cancellationToken).ConfigureAwait(false);
        if (detail is null)
        {
            // Artists that Apple Music localizes (花澤香菜 -> 花泽香菜) or has no image for at all
            // are covered by the separate Netease provider, which shows up as its own source in
            // Emby's picker.
            _logger.Info("Apple Music: no artist named '{0}' was found, no images to offer", artist.Name);
            return new List<RemoteImageInfo>();
        }

        // Apple Music ships an avatar for most artists, a wide artwork (2:1, the only genuinely
        // landscape image it has) for some, and a logo for a few - so each type is offered only
        // when it actually exists.
        var images = new List<RemoteImageInfo>();

        if (!string.IsNullOrEmpty(detail.ImageUrl))
        {
            images.Add(CreateImageInfo(detail.ImageUrl!, ImageType.Primary, 1400, 1400, "1400x1400cc", "100x100cc"));
        }

        if (!string.IsNullOrEmpty(detail.WideImageUrl))
        {
            images.Add(CreateImageInfo(detail.WideImageUrl!, ImageType.Backdrop, 2000, 1125, "2000x1125bb", "400x225bb"));
        }

        if (!string.IsNullOrEmpty(detail.LogoUrl))
        {
            // Logos are wide (实测 1000x353) and must stay png to keep their transparency.
            images.Add(CreateImageInfo(detail.LogoUrl!, ImageType.Logo, 1000, 353, "1000x1000bb", "200x200bb", "png"));
        }

        _logger.Info(
            "Apple Music: offering {0} image(s) for '{1}' (matched '{2}')",
            images.Count,
            artist.Name,
            detail.Name);

        return images;
    }

    /// <summary>
    /// Finds the Apple Music artist backing a library artist: by the stored ID when there is one,
    /// otherwise by name. The ID is preferred because it is exact, but a stored ID can point at
    /// an artist Apple Music has no image for (the library artist 瑞葵 carries such an ID), so
    /// both routes end up checking the detail page.
    /// </summary>
    /// <param name="artist">Library artist.</param>
    /// <param name="appleMusicId">Stored Apple Music ID, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artist detail, or null when nothing matching was found.</returns>
    private async Task<AppleMusicArtist?> FindArtistAsync(MusicArtist artist, string? appleMusicId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(appleMusicId))
        {
            _logger.Info("Apple Music: using ID {0} for artist image lookup", appleMusicId);
            var byId = await _metadataSource.GetArtistAsync(appleMusicId!, cancellationToken).ConfigureAwait(false);
            if (byId is not null && HasAnyImage(byId))
            {
                return byId;
            }

            _logger.Info("Apple Music: artist ID {0} has no usable image, falling back to a name lookup", appleMusicId);
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
            return null;
        }

        // A search hit only carries the avatar: the wide artwork and the logo live on the detail
        // page, so fetch it when the name lookup is what found the artist.
        return await _metadataSource.GetArtistAsync(match.Id, cancellationToken).ConfigureAwait(false) ?? match;
    }

    private static bool HasAnyImage(AppleMusicArtist artist)
    {
        return !string.IsNullOrEmpty(artist.ImageUrl)
               || !string.IsNullOrEmpty(artist.WideImageUrl)
               || !string.IsNullOrEmpty(artist.LogoUrl);
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

    private RemoteImageInfo CreateImageInfo(string templateUrl, ImageType type, int width, int height, string detailSize, string thumbnailSize, string extension = "jpg")
    {
        return new RemoteImageInfo
        {
            Height = height,
            Width = width,
            ProviderName = Name,
            ThumbnailUrl = PluginUtils.UpdateImageSize(templateUrl, thumbnailSize, extension),
            Type = type,
            Url = PluginUtils.UpdateImageSize(templateUrl, detailSize, extension),
        };
    }
}
