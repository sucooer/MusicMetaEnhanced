using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Itunes;

/// <summary>
/// An album as returned by the iTunes Search API.
/// </summary>
/// <param name="Id">Collection id - the same Adam id the Apple Music web pages use.</param>
/// <param name="Name">Album name in the storefront's own script.</param>
/// <param name="ArtistName">Album artist name in the storefront's own script.</param>
/// <param name="ArtistId">Artist Adam id.</param>
/// <param name="Year">Release year.</param>
/// <param name="Genre">Primary genre name.</param>
/// <param name="TrackCount">Number of tracks.</param>
/// <param name="Description">Long description, when the storefront ships one.</param>
public sealed record ItunesAlbumData(
    string Id,
    string Name,
    string? ArtistName,
    string? ArtistId,
    int? Year,
    string? Genre,
    int? TrackCount,
    string? Description);

/// <summary>
/// A track as returned by the iTunes Search API.
/// </summary>
/// <param name="Id">Track Adam id.</param>
/// <param name="Name">Track name in the storefront's own script.</param>
/// <param name="TrackNumber">Position on the disc.</param>
/// <param name="DiscNumber">Disc number.</param>
/// <param name="DurationMs">Duration in milliseconds.</param>
/// <param name="ArtistName">Track artist name.</param>
public sealed record ItunesTrackData(
    string Id,
    string Name,
    int? TrackNumber,
    int? DiscNumber,
    int? DurationMs,
    string? ArtistName);

/// <summary>
/// Reads album metadata from the iTunes Search API
/// (https://itunes.apple.com/lookup, https://itunes.apple.com/search).
/// The country parameter selects the storefront regardless of where the server runs,
/// which the Apple Music web pages refuse to do (they geo-redirect by IP). Album data
/// for a Japanese library therefore comes from country=jp with the original kana
/// spellings, while the same request through the web pages would return localized names.
/// </summary>
public class ItunesAlbumSource
{
    private const string LookupUrl = "https://itunes.apple.com/lookup?id={0}&country={1}&entity=song&limit=200";
    private const string SearchUrl = "https://itunes.apple.com/search?term={0}&country={1}&media=music&entity=album&limit=10";

    private readonly ISimpleHttpClient _httpClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItunesAlbumSource"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public ItunesAlbumSource(ISimpleHttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Looks an album up by its Adam id.
    /// </summary>
    /// <param name="albumId">Album Adam id.</param>
    /// <param name="storefront">Storefront code, e.g. "jp".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album data, or null when the id is unknown in that storefront.</returns>
    public async Task<ItunesAlbumData?> LookupAsync(string albumId, string storefront, CancellationToken cancellationToken)
    {
        var url = string.Format(LookupUrl, Uri.EscapeDataString(albumId), storefront);
        var collections = await GetCollectionsAsync(url, cancellationToken).ConfigureAwait(false);
        return collections.FirstOrDefault();
    }

    /// <summary>
    /// Reads the track list of an album.
    /// The lookup URL already asks for <c>entity=song</c>, so the response contains the
    /// collection followed by one entry per track. Only some storefronts actually ship
    /// them - cn, for example, lists the album but none of its songs.
    /// </summary>
    /// <param name="albumId">Album Adam id.</param>
    /// <param name="storefront">Storefront code, e.g. "jp".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Track list, possibly empty.</returns>
    public async Task<IReadOnlyList<ItunesTrackData>> GetTracksAsync(string albumId, string storefront, CancellationToken cancellationToken)
    {
        var url = string.Format(LookupUrl, Uri.EscapeDataString(albumId), storefront);
        return await GetTracksFromUrlAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Looks a single track up by its Adam id.
    /// </summary>
    /// <param name="trackId">Track Adam id.</param>
    /// <param name="storefront">Storefront code, e.g. "jp".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Track data, or null when the id is unknown in that storefront.</returns>
    public async Task<ItunesTrackData?> LookupTrackAsync(string trackId, string storefront, CancellationToken cancellationToken)
    {
        var url = string.Format(LookupUrl, Uri.EscapeDataString(trackId), storefront);
        var tracks = await GetTracksFromUrlAsync(url, cancellationToken).ConfigureAwait(false);
        return tracks.FirstOrDefault();
    }

    /// <summary>
    /// Searches albums in a storefront.
    /// </summary>
    /// <param name="term">Free text search term.</param>
    /// <param name="storefront">Storefront code, e.g. "jp".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album data list, possibly empty.</returns>
    public async Task<IReadOnlyList<ItunesAlbumData>> SearchAsync(string term, string storefront, CancellationToken cancellationToken)
    {
        var url = string.Format(SearchUrl, Uri.EscapeDataString(term), storefront);
        return await GetCollectionsAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches an iTunes API response and materializes its collection entries. The data
    /// must be extracted while the JSON document is alive: JsonElement is only a view
    /// into it and dies with the document.
    /// </summary>
    /// <param name="url">Request URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album data list, possibly empty.</returns>
    private async Task<List<ItunesAlbumData>> GetCollectionsAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(url, new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return new List<ItunesAlbumData>();
            }

            return results.EnumerateArray()
                .Where(element => WrapperType(element) == "collection")
                .Select(ToData)
                .Where(data => data is not null)
                .Select(data => data!)
                .ToList();
        }
        catch (Exception exception)
        {
            // The iTunes API is one source among several - a failure must never break
            // a metadata refresh; the caller falls back to the web data source.
            _logger.ErrorException("iTunes: request to {0} failed", exception, url);
            return new List<ItunesAlbumData>();
        }
    }

    /// <summary>
    /// Fetches an iTunes API response and materializes its track entries.
    /// </summary>
    /// <param name="url">Request URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Track list, possibly empty.</returns>
    private async Task<List<ItunesTrackData>> GetTracksFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(url, new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return new List<ItunesTrackData>();
            }

            return results.EnumerateArray()
                .Where(element => WrapperType(element) == "track")
                .Select(ToTrackData)
                .Where(data => data is not null)
                .Select(data => data!)
                .ToList();
        }
        catch (Exception exception)
        {
            // Same rule as the album path: a failing source must never break a refresh.
            _logger.ErrorException("iTunes: request to {0} failed", exception, url);
            return new List<ItunesTrackData>();
        }
    }

    private static ItunesTrackData? ToTrackData(JsonElement element)
    {
        int? Number(string name)
        {
            return element.TryGetProperty(name, out var value)
                   && value.ValueKind == JsonValueKind.Number
                   && value.TryGetInt32(out var number)
                ? number
                : null;
        }

        // Adam ids exceed int32 (6 799 021 921 is a perfectly normal track id), so the
        // id has to be read as int64 - GetInt32 silently throws on those.
        long? Long(string name)
        {
            return element.TryGetProperty(name, out var value)
                   && value.ValueKind == JsonValueKind.Number
                   && value.TryGetInt64(out var number)
                ? number
                : null;
        }

        string? Text(string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        var id = Long("trackId");
        var name = Text("trackName");
        if (id is null || string.IsNullOrEmpty(name))
        {
            return null;
        }

        return new ItunesTrackData(
            id.Value.ToString(),
            name!,
            Number("trackNumber"),
            Number("discNumber"),
            Number("trackTimeMillis"),
            Text("artistName"));
    }

    private static string? WrapperType(JsonElement element)
    {
        return element.TryGetProperty("wrapperType", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static ItunesAlbumData? ToData(JsonElement element)
    {
        // Note: the iTunes API ships ids and counts as JSON numbers, only names as strings.
        string? Text(string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        string? IdText(string name)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ValueKind == JsonValueKind.Number ? value.GetInt64().ToString() : null;
        }

        var id = IdText("collectionId");
        var name = Text("collectionName");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        int? year = null;
        var releaseDate = Text("releaseDate");
        if (!string.IsNullOrEmpty(releaseDate) && DateTime.TryParse(releaseDate, out var parsed))
        {
            year = parsed.Year;
        }

        int? trackCount = null;
        if (element.TryGetProperty("trackCount", out var tracks) && tracks.ValueKind == JsonValueKind.Number)
        {
            trackCount = tracks.GetInt32();
        }

        return new ItunesAlbumData(
            id!,
            name!,
            Text("artistName"),
            IdText("artistId"),
            year,
            Text("primaryGenreName"),
            trackCount,
            Text("longDescription") ?? Text("description"));
    }
}
