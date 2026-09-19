using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using HttpRequestOptions = MediaBrowser.Common.Net.HttpRequestOptions;

namespace ScrapeTest;

/// <summary>
/// Minimal <see cref="IHttpClient"/> implementation for out-of-server testing.
/// </summary>
internal sealed class HttpClientStub : IHttpClient
{
    private static readonly HttpClient Client = Create();

    public async Task<HttpResponseInfo> GetResponse(HttpRequestOptions options)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.Url);
        var response = await Client.SendAsync(request, options.CancellationToken).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(options.CancellationToken).ConfigureAwait(false);

        return new HttpResponseInfo
        {
            Content = new MemoryStream(bytes),
            StatusCode = response.StatusCode,
            ContentType = response.Content.Headers.ContentType?.ToString(),
            ContentLength = bytes.Length,
            ResponseUrl = options.Url,
        };
    }

    public async Task<Stream> Get(HttpRequestOptions options)
    {
        var response = await GetResponse(options).ConfigureAwait(false);
        return response.Content;
    }

    public Task<HttpResponseInfo> SendAsync(HttpRequestOptions options, string httpMethod)
    {
        return GetResponse(options);
    }

    public Task<HttpResponseInfo> Post(HttpRequestOptions options)
    {
        return GetResponse(options);
    }

    public Task<string> GetTempFile(HttpRequestOptions options)
    {
        throw new NotImplementedException();
    }

    public Task<HttpResponseInfo> GetTempFileResponse(HttpRequestOptions options)
    {
        throw new NotImplementedException();
    }

    public IDisposable GetConnectionContext(HttpRequestOptions options)
    {
        return new MemoryStream();
    }

    private static HttpClient Create()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        return client;
    }
}
