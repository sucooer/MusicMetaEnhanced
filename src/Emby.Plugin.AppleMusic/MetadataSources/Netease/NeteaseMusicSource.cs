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

    /// <summary>
    /// Size requested for artist images, matching what the Apple Music provider asks for.
    /// </summary>
    private const int ImageSize = 1400;

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
    /// Default API base URL. Used as the configuration default, and also when the plugin has
    /// not been loaded (tools, tests) so the same source can be exercised outside Emby.
    /// </summary>
    public const string DefaultApiBaseUrl = "https://api.520717.xyz";

    /// <summary>
    /// Gets the configured API base URL, or null when the feature is disabled.
    /// </summary>
    public static string? ConfiguredBaseUrl
    {
        get
        {
            var url = Plugin.Instance?.Configuration?.NeteaseApiBaseUrl ?? DefaultApiBaseUrl;
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
            // A biography goes straight into the library, so it needs an exact name match.
            var artist = await FindArtistAsync(baseUrl, artistName, forImage: false, cancellationToken).ConfigureAwait(false);
            if (artist is null)
            {
                return null;
            }

            var bio = await GetBioByIdAsync(baseUrl, artist.Id, artistName, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Gets the URL of an artist image, sized the same way the Apple Music provider sizes its
    /// images. Netease stores the artist's original name, so this covers artists Apple Music
    /// localizes away from the library name (花澤香菜 -> 花泽香菜).
    /// </summary>
    /// <param name="artistName">Artist name as stored in the library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Image URL, or null when nothing suitable was found.</returns>
    public async Task<string?> GetArtistImageUrlAsync(string artistName, CancellationToken cancellationToken)
    {
        var baseUrl = ConfiguredBaseUrl;
        if (baseUrl is null || string.IsNullOrWhiteSpace(artistName))
        {
            return null;
        }

        try
        {
            // An image is only shown to the person searching for it, so a suffixed name
            // ("瑞葵(mizuki)") is accepted here.
            var match = await FindArtistAsync(baseUrl, artistName, forImage: true, cancellationToken).ConfigureAwait(false);
            if (match?.ImageUrl is null)
            {
                _logger.Info("Netease: no artist image for '{0}'", artistName);
                return null;
            }

            _logger.Info("Netease: found an artist image for '{0}' (ID {1})", artistName, match.Id);
            return WithSize(match.ImageUrl, ImageSize);
        }
        catch (Exception exception)
        {
            // A missing or broken Netease API must never break an image lookup.
            _logger.ErrorException("Netease: failed to fetch an artist image for '{0}'", exception, artistName);
            return null;
        }
    }

    /// <summary>
    /// Gets every spelling Netease knows for an artist: its own name, aliases and translated
    /// names. Apple Music localizes some artist names (奥華子 is shown as "Hanako Oku"), and
    /// Netease keeps the original one - so these spellings act as a bridge between the two.
    /// </summary>
    /// <param name="artistName">Artist name as stored in the library.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Alternative spellings, possibly empty.</returns>
    public async Task<IReadOnlyList<string>> GetArtistAliasNamesAsync(string artistName, CancellationToken cancellationToken)
    {
        var baseUrl = ConfiguredBaseUrl;
        if (baseUrl is null || string.IsNullOrWhiteSpace(artistName))
        {
            return Array.Empty<string>();
        }

        try
        {
            var artist = await FindArtistAsync(baseUrl, artistName, forImage: false, cancellationToken).ConfigureAwait(false);
            return artist?.Aliases ?? Array.Empty<string>();
        }
        catch (Exception exception)
        {
            // Aliases are only the third matching attempt - a failure here must never
            // break an otherwise working metadata refresh.
            _logger.ErrorException("Netease: failed to fetch aliases for '{0}'", exception, artistName);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Appends the Netease size parameter, which is how their CDN picks the resolution.
    /// An existing size parameter is replaced.
    /// </summary>
    /// <param name="imageUrl">Image URL, with or without a query string.</param>
    /// <param name="size">Requested square size in pixels.</param>
    /// <returns>URL of the sized image.</returns>
    public static string WithSize(string imageUrl, int size)
    {
        return $"{StripSizeParameter(imageUrl)}?param={size}y{size}";
    }

    /// <summary>
    /// An artist as found in Netease.
    /// </summary>
    /// <param name="Id">Netease artist id.</param>
    /// <param name="ImageUrl">Artist image URL without a size parameter.</param>
    /// <param name="Aliases">Every spelling Netease knows for this artist.</param>
    private sealed record NeteaseArtistMatch(string Id, string? ImageUrl, IReadOnlyList<string> Aliases);

    private async Task<NeteaseArtistMatch?> FindArtistAsync(string baseUrl, string artistName, bool forImage, CancellationToken cancellationToken)
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
            if (!IsSameArtist(artist, artistName, forImage))
            {
                continue;
            }

            var id = artist.TryGetProperty("id", out var idElement) ? idElement.ToString() : null;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            // picUrl sometimes already carries its own ?param= size, which must be replaced
            // rather than appended to.
            var picture = ArtistField(artist, "picUrl");
            var imageUrl = string.IsNullOrWhiteSpace(picture)
                ? null
                : StripSizeParameter(picture!);

            return new NeteaseArtistMatch(id!, imageUrl, CollectSpellings(artist));
        }

        return null;
    }

    private static IReadOnlyList<string> CollectSpellings(JsonElement artist)
    {
        var names = new List<string>();
        foreach (var field in new[] { "name", "alias", "transNames" })
        {
            if (!artist.TryGetProperty(field, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                names.Add(value.GetString()!);
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        names.Add(item.GetString()!);
                    }
                }
            }
        }

        return names;
    }

    private static string StripSizeParameter(string url)
    {
        var index = url.IndexOf('?', StringComparison.Ordinal);
        return index < 0 ? url : url[..index];
    }

    private static bool IsSameArtist(JsonElement artist, string artistName, bool forImage)
    {
        if (Matches(ArtistField(artist, "name"), artistName, forImage))
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
                if (value.ValueKind == JsonValueKind.String && Matches(value.GetString(), artistName, forImage))
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

    private static bool Matches(string? candidate, string artistName, bool forImage)
    {
        if (forImage)
        {
            // An image is only shown to the person who searched for it, so a qualifier is fine
            // in either direction ("瑞葵(mizuki)" for the library's "瑞葵").
            return TitleMatcher.IsSameNameIgnoringSuffix(candidate, artistName);
        }

        // A biography is written into the library. An exact name passes, and so does a provider
        // name that merely adds a qualifier ("瑞葵(mizuki)") - the qualifier only refines the
        // same name. A *library* name carrying the qualifier means the item is something
        // narrower (a character credit), and Netease data for the plain name may not describe
        // it, so that is skipped.
        return TitleMatcher.IsSameTitle(candidate, artistName)
               || TitleMatcher.IsProviderNameQualified(candidate, artistName);
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
