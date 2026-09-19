using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models.Requests;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models.Responses;
using Emby.Plugin.MusicMetaEnhanced.Utils;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient;

/// <summary>
/// Apple Music JSON API client.
/// </summary>
public class DefaultApiClient
{
    private const string ApiBaseUrl = "https://amp-api-edge.music.apple.com/v1/catalog";
    private const string Origin = "https://music.apple.com";
    private const string Referer = "https://music.apple.com/";

    private readonly ISimpleHttpClient _httpClient;
    private readonly IAppleMusicTokenProvider _tokenProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">Underlying HTTP client.</param>
    /// <param name="tokenProvider">Bearer token provider.</param>
    /// <param name="logger">Logger.</param>
    public DefaultApiClient(ISimpleHttpClient httpClient, IAppleMusicTokenProvider tokenProvider, ILogger logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    /// <summary>
    /// Search Apple Music for albums and artists.
    /// </summary>
    /// <param name="request">Search request parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response.</returns>
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var url = $"{ApiBaseUrl}/{PluginUtils.Storefront}/search?{request.ToQueryString()}";
        var headers = await CreateRequestHeadersAsync(cancellationToken).ConfigureAwait(false);

        _logger.Debug("Apple Music: searching API {0}", url);
        var response = await _httpClient.GetAsync<SearchResponse>(url, headers, cancellationToken).ConfigureAwait(false);
        return response ?? new SearchResponse();
    }

    /// <summary>
    /// Gets an album by Apple Music ID.
    /// </summary>
    /// <param name="albumId">Apple Music album ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album data or null when not found.</returns>
    public async Task<SearchResult?> GetAlbumAsync(string albumId, CancellationToken cancellationToken)
    {
        var url = $"{ApiBaseUrl}/{PluginUtils.Storefront}/albums/{albumId}";
        var headers = await CreateRequestHeadersAsync(cancellationToken).ConfigureAwait(false);

        _logger.Debug("Apple Music: fetching album {0}", url);
        var response = await _httpClient.GetAsync<ResourceResponse>(url, headers, cancellationToken).ConfigureAwait(false);
        return response?.Data.FirstOrDefault();
    }

    /// <summary>
    /// Gets an artist by Apple Music ID.
    /// </summary>
    /// <param name="artistId">Apple Music artist ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artist data or null when not found.</returns>
    public async Task<SearchResult?> GetArtistAsync(string artistId, CancellationToken cancellationToken)
    {
        var url = $"{ApiBaseUrl}/{PluginUtils.Storefront}/artists/{artistId}";
        var headers = await CreateRequestHeadersAsync(cancellationToken).ConfigureAwait(false);

        _logger.Debug("Apple Music: fetching artist {0}", url);
        var response = await _httpClient.GetAsync<ResourceResponse>(url, headers, cancellationToken).ConfigureAwait(false);
        return response?.Data.FirstOrDefault();
    }

    private async Task<Dictionary<string, string>> CreateRequestHeadersAsync(CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, string>
        {
            { "Authorization", "Bearer " + token },
            { "Origin", Origin },
            { "Referer", Referer },
        };
    }
}
