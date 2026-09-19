using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient;

/// <summary>
/// Minimal HTTP client abstraction used by the JSON metadata source.
/// </summary>
public interface ISimpleHttpClient
{
    /// <summary>
    /// Performs a GET request and deserializes the JSON response.
    /// </summary>
    /// <typeparam name="T">Response type.</typeparam>
    /// <param name="url">Request URL.</param>
    /// <param name="headers">Additional request headers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deserialized response.</returns>
    Task<T?> GetAsync<T>(string url, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a GET request and returns the response body as a string.
    /// </summary>
    /// <param name="url">Request URL.</param>
    /// <param name="headers">Additional request headers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response body.</returns>
    Task<string> GetStringAsync(string url, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
}
