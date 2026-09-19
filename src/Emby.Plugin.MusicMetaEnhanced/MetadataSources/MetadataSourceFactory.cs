using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Web;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources;

/// <summary>
/// Factory for creating metadata sources.
/// </summary>
public static class MetadataSourceFactory
{
    /// <summary>
    /// Creates a metadata source instance based on the plugin configuration.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClient">HTTP client.</param>
    /// <returns>A metadata source instance.</returns>
    public static IMetadataSource Create(ILogger logger, IHttpClient httpClient)
    {
        if (Plugin.Instance?.Configuration?.UseJsonSource == true)
        {
            return CreateJsonSource(logger);
        }

        return new WebMetadataSource(logger, httpClient);
    }

    private static JsonMetadataSource CreateJsonSource(ILogger logger)
    {
        var httpClient = new SimpleHttpClient();
        var tokenProvider = new WebPlayTokenProvider(httpClient, logger);
        var apiClient = new DefaultApiClient(httpClient, tokenProvider, logger);
        return new JsonMetadataSource(apiClient, logger);
    }
}
