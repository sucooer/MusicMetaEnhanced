using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient;

/// <summary>
/// <see cref="ISimpleHttpClient"/> implementation based on <see cref="System.Net.Http.HttpClient"/>.
/// </summary>
public class SimpleHttpClient : ISimpleHttpClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient Client = CreateClient();

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string url, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        var content = await GetStringAsync(url, headers, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(content, SerializerOptions);
    }

    /// <inheritdoc />
    public async Task<string> GetStringAsync(string url, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var response = await Client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            // Biographies are a nice to have: never let a slow or dead source stall a
            // metadata refresh.
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }
}
