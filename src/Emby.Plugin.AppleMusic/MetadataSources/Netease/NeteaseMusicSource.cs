using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient;
using Emby.Plugin.AppleMusic.Utils;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.AppleMusic.MetadataSources.Netease;

/// <summary>
/// Reads Chinese artist biographies from a NeteaseCloudMusicApi instance
/// (https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced), either self hosted
/// or a public one. Apple Music only ships artist biographies for a small number of
/// artists, so this is used to fill the gap with a Chinese source.
/// </summary>
public class NeteaseMusicSource
{
    /// <summary>
    /// Maximum length of the biography that is written into the library.
    /// </summary>
    private const int MaxBioLength = 8000;

    /// <summary>
    /// Minimum length of a biography that is accepted. Netease biographies are partly
    /// user submitted, so short entries are often junk such as a profile link or a
    /// single word, which is worse than having no biography at all.
    /// </summary>
    private const int MinBioLength = 20;

    /// <summary>
    /// Netease "artist" search type.
    /// </summary>
    private const int SearchTypeArtist = 100;

    private readonly ILogger _logger;
    private readonly ISimpleHttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="NeteaseMusicSource"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClient">HTTP client.</param>
    public NeteaseMusicSource(ILogger logger, ISimpleHttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Gets the configured API base URL, or null when the feature is disabled.
    /// </summary>
    public static string? ConfiguredBaseUrl
    {
        get
        {
            var url = Plugin.Instance?.Configuration?.NeteaseApiBaseUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            return url.Trim().TrimEnd('/');
        }
    }

    /// <summary>
    /// Gets a Chinese artist biography.
    /// </summary>
    /// <param name="artistName">Artist name as stored in the library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Biography text, or null when nothing suitable was found.</returns>
    public async Task<string?> GetArtistBioAsync(string artistName, CancellationToken cancellationToken)
    {
        var baseUrl = ConfiguredBaseUrl;
        if (baseUrl is null || string.IsNullOrWhiteSpace(artistName))
        {
            return null;
        }

        try
        {
            var artistId = await FindArtistIdAsync(baseUrl, artistName, cancellationToken).ConfigureAwait(false);
            if (artistId is null)
            {
                return null;
            }

            var bio = await GetBioByIdAsync(baseUrl, artistId, artistName, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(bio) || bio!.Trim().Length < MinBioLength)
            {
                _logger.Info("Netease: no usable biography for artist '{0}'", artistName);
                return null;
            }

            bio = bio.Trim();
            _logger.Info("Netease: found a {0} character biography for artist '{1}'", bio.Length, artistName);
            return bio.Length > MaxBioLength ? bio.Substring(0, MaxBioLength) : bio;
        }
        catch (Exception exception)
        {
            // A missing or broken Netease API must never break a metadata refresh.
            _logger.ErrorException("Netease: failed to fetch a biography for '{0}'", exception, artistName);
            return null;
        }
    }

    private async Task<string?> FindArtistIdAsync(string baseUrl, string artistName, CancellationToken cancellationToken)
    {
        var url = $"{baseUrl}/search?keywords={Uri.EscapeDataString(artistName)}&type={SearchTypeArtist}&limit=10";
        var headers = new Dictionary<string, string>();

        var json = await _httpClient.GetStringAsync(url, headers, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("artists", out var artists)
            || artists.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var artist in artists.EnumerateArray())
        {
            if (!IsSameArtist(artist, artistName))
            {
                continue;
            }

            var id = artist.TryGetProperty("id", out var idElement) ? idElement.ToString() : null;
            if (!string.IsNullOrEmpty(id))
            {
                return id;
            }
        }

        return null;
    }

    private static bool IsSameArtist(JsonElement artist, string artistName)
    {
        if (Matches(ArtistField(artist, "name"), artistName))
        {
            return true;
        }

        // Netease stores alternative spellings (romaji, translations) as aliases and
        // translations - e.g. 河野万里奈 is found as name 河野マリナ + transName 河野万里奈.
        foreach (var field in new[] { "alias", "transNames" })
        {
            if (!artist.TryGetProperty(field, out var values) || values.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String && Matches(value.GetString(), artistName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? ArtistField(JsonElement artist, string name)
    {
        return artist.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool Matches(string? candidate, string artistName)
    {
        return TitleMatcher.IsSameTitle(candidate, artistName);
    }

    private async Task<string?> GetBioByIdAsync(string baseUrl, string artistId, string artistName, CancellationToken cancellationToken)
    {
        var url = $"{baseUrl}/artist/desc?id={Uri.EscapeDataString(artistId)}";
        var json = await _httpClient.GetStringAsync(url, new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 200)
        {
            return null;
        }

        // The full biography lives in "introduction"; "briefDesc" is only a teaser.
        if (root.TryGetProperty("introduction", out var introduction) && introduction.ValueKind == JsonValueKind.Array)
        {
            var blocks = introduction.EnumerateArray()
                .Select(block => block.TryGetProperty("tx", out var tx) && tx.ValueKind == JsonValueKind.String ? tx.GetString() : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!.Trim())
                .ToList();

            if (blocks.Count > 0)
            {
                // The first block is the main biography, the rest are usually release notes.
                return blocks[0];
            }
        }

        var brief = root.TryGetProperty("briefDesc", out var briefDesc) && briefDesc.ValueKind == JsonValueKind.String
            ? briefDesc.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(brief))
        {
            return brief!.Trim();
        }

        _logger.Debug("Netease: answer for artist {0} ({1}) contained no biography", artistName, artistId);
        return null;
    }
}
