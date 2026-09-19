using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient;

/// <summary>
/// Provides the Apple Music web player bearer token.
/// The token is scraped from the Apple Music website script bundle and cached
/// until shortly before it expires.
/// </summary>
public class WebPlayTokenProvider : IAppleMusicTokenProvider
{
    private const string HomePageUrl = "https://music.apple.com/";
    private const string AssetsBaseUrl = "https://music.apple.com";
    private const string WebPlayKeyId = "WebPlayKid";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromHours(24);

    private readonly ISimpleHttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _token;
    private DateTime _expiresAt = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebPlayTokenProvider"/> class.
    /// </summary>
    /// <param name="httpClient">Underlying HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public WebPlayTokenProvider(ISimpleHttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && IsTokenValid())
        {
            return _token;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_token is not null && IsTokenValid())
            {
                return _token;
            }

            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return _token ?? string.Empty;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsTokenValid()
    {
        return DateTime.UtcNow < _expiresAt - RefreshMargin;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Apple Music: fetching web player token");

        var headers = new Dictionary<string, string>();
        var html = await _httpClient.GetStringAsync(HomePageUrl, headers, cancellationToken).ConfigureAwait(false);

        var bundleMatch = BundleRegex().Match(html);
        if (!bundleMatch.Success)
        {
            _logger.Error("Apple Music: could not locate the web player script bundle");
            throw new InvalidOperationException("Failed to locate the Apple Music web player script bundle.");
        }

        var bundleUrl = AssetsBaseUrl + bundleMatch.Value;
        var bundle = await _httpClient.GetStringAsync(bundleUrl, headers, cancellationToken).ConfigureAwait(false);

        var token = ExtractWebPlayToken(bundle);
        if (token is null)
        {
            _logger.Error("Apple Music: could not extract the web player token from bundle {0}", bundleUrl);
            throw new InvalidOperationException("Failed to extract the Apple Music web player token.");
        }

        _token = token;
        _expiresAt = ReadExpiry(token).UtcDateTime;
        _logger.Info("Apple Music: web player token refreshed, expires at {0}", _expiresAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static string? ExtractWebPlayToken(string bundle)
    {
        foreach (Match match in JwtRegex().Matches(bundle))
        {
            var token = match.Value;
            if (string.Equals(GetHeaderKeyId(token), WebPlayKeyId, StringComparison.Ordinal))
            {
                return token;
            }
        }

        return null;
    }

    private static string? GetHeaderKeyId(string token)
    {
        var header = DecodeSegment(token, 0);
        if (header is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(header);
        return document.RootElement.TryGetProperty("kid", out var kid) ? kid.GetString() : null;
    }

    private static DateTimeOffset ReadExpiry(string token)
    {
        var payload = DecodeSegment(token, 1);
        if (payload is not null)
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }

        return DateTimeOffset.MinValue;
    }

    private static string? DecodeSegment(string token, int index)
    {
        var parts = token.Split('.');
        if (parts.Length <= index)
        {
            return null;
        }

        // JWT uses base64url, which swaps '+' and '/' for '-' and '_'
        var value = parts[index].Replace('-', '+').Replace('_', '/');

        // base64 requires the length to be a multiple of 4
        var padding = (4 - (value.Length % 4)) % 4;
        if (padding > 0)
        {
            value = value.PadRight(value.Length + padding, '=');
        }

        var bytes = Convert.FromBase64String(value);
        return Encoding.UTF8.GetString(bytes);
    }

    private static Regex BundleRegex()
    {
        return new Regex("/assets/index~[A-Za-z0-9]+\\.js", RegexOptions.Compiled);
    }

    private static Regex JwtRegex()
    {
        return new Regex("eyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+", RegexOptions.Compiled);
    }
}
