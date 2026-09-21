using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MusicMetaEnhanced.Dtos;
using Emby.Plugin.MusicMetaEnhanced.Utils;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Web;

/// <summary>
/// Apple Music web metadata source.
/// This source reads the structured page data embedded in the Apple Music website.
/// </summary>
public class WebMetadataSource : IMetadataSource
{
    /// <summary>
    /// Maximum number of search results returned to Emby.
    /// </summary>
    private const int MaxSearchResults = 10;

    /// <summary>
    /// Number of leading search results that are enriched with detail page data.
    /// </summary>
    private const int EnrichCount = 5;

    /// <summary>
    /// Content type labels Apple Music puts in front of artist names in some page variants.
    /// </summary>
    private static readonly string[] ContentTypeLabels =
    {
        "专辑", "单曲", "合辑", "精选集", "现场版", "EP", "Album", "Single", "Compilation", "Deluxe", "Live",
    };

    private readonly ILogger _logger;
    private readonly IHttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebMetadataSource"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="httpClient">HTTP client.</param>
    public WebMetadataSource(ILogger logger, IHttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<List<IAppleMusicItem>> SearchAsync(string searchTerm, ItemType itemType, CancellationToken cancellationToken)
    {
        _logger.Info("Apple Music: searching for {0} with term: {1}", itemType, searchTerm);

        var encodedTerm = Uri.EscapeDataString(searchTerm);
        var searchUrl = $"{PluginUtils.AppleMusicBaseUrl}/search?term={encodedTerm}";

        var html = await GetPageAsync(searchUrl, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return new List<IAppleMusicItem>();
        }

        using var document = AppleMusicPageParser.Parse(html);
        if (document is null)
        {
            _logger.Warn("Apple Music: search page does not contain any usable data");
            return new List<IAppleMusicItem>();
        }

        var wantedKind = itemType == ItemType.Album ? "album" : "artist";
        var found = new List<JsonElement>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in AppleMusicPageParser.GetSections(document))
        {
            foreach (var item in AppleMusicPageParser.GetItems(section))
            {
                var kind = AppleMusicPageParser.GetStringPath(item, "contentDescriptor", "kind");
                if (!string.Equals(kind, wantedKind, StringComparison.Ordinal))
                {
                    continue;
                }

                var id = AppleMusicPageParser.GetIdPath(item, "contentDescriptor", "identifiers", "storeAdamID");
                if (string.IsNullOrEmpty(id) || !seenIds.Add(id!))
                {
                    continue;
                }

                found.Add(item);
                if (found.Count >= MaxSearchResults)
                {
                    break;
                }
            }

            if (found.Count >= MaxSearchResults)
            {
                break;
            }
        }

        _logger.Info("Apple Music: found {0} {1} for search term {2}", found.Count, wantedKind, searchTerm);

        if (itemType == ItemType.Album)
        {
            return await MapSearchAlbums(found, cancellationToken).ConfigureAwait(false);
        }

        return found.Select(MapSearchArtist).Cast<IAppleMusicItem>().ToList();
    }

    /// <inheritdoc />
    public async Task<AppleMusicAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken)
    {
        var albumUrl = $"{PluginUtils.AppleMusicBaseUrl}/album/{albumId}";
        var html = await GetPageAsync(albumUrl, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        using var document = AppleMusicPageParser.Parse(html);
        if (document is null)
        {
            _logger.Warn("Apple Music: album page {0} does not contain any usable data", albumUrl);
            return null;
        }

        var item = FindFirstItem(document, "containerDetailHeaderLockup", "album-detail-header");
        if (item is null)
        {
            _logger.Debug("Apple Music: album detail header not found on {0}", albumUrl);
            return null;
        }

        var album = MapAlbumDetail(item.Value, albumId, albumUrl);
        album.Tracks = ParseTracks(document);
        return album;
    }

    /// <inheritdoc />
    public async Task<AppleMusicArtist?> GetArtistAsync(string artistId, CancellationToken cancellationToken)
    {
        var artistUrl = $"{PluginUtils.AppleMusicBaseUrl}/artist/{artistId}";
        var html = await GetPageAsync(artistUrl, cancellationToken).ConfigureAwait(false);
        if (html is null)
        {
            return null;
        }

        using var document = AppleMusicPageParser.Parse(html);
        if (document is null)
        {
            _logger.Warn("Apple Music: artist page {0} does not contain any usable data", artistUrl);
            return null;
        }

        var item = FindFirstItem(document, "artistDetailHeader", "artistdetailheader");
        if (item is null)
        {
            _logger.Debug("Apple Music: artist detail header not found on {0}", artistUrl);
            return null;
        }

        return MapArtistDetail(item.Value, artistId, artistUrl);
    }

    private async Task<List<IAppleMusicItem>> MapSearchAlbums(List<JsonElement> items, CancellationToken cancellationToken)
    {
        var albums = new List<AppleMusicAlbum>();

        for (var i = 0; i < items.Count; i++)
        {
            var album = MapSearchAlbum(items[i]);
            if (album is null)
            {
                continue;
            }

            // The search result itself carries no release date or description, so the first
            // few results are enriched with data from their detail page.
            if (i < EnrichCount && !string.IsNullOrEmpty(album.Id))
            {
                var detail = await GetAlbumAsync(album.Id, cancellationToken).ConfigureAwait(false);
                if (detail is not null)
                {
                    album.ReleaseDate = detail.ReleaseDate;
                    album.About = album.About ?? detail.About;
                }
            }

            albums.Add(album);
        }

        return albums.Cast<IAppleMusicItem>().ToList();
    }

    private AppleMusicAlbum? MapSearchAlbum(JsonElement item)
    {
        var name = GetFirstTitle(item, "titleLinks") ?? AppleMusicPageParser.GetString(item, "title");
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var id = AppleMusicPageParser.GetIdPath(item, "contentDescriptor", "identifiers", "storeAdamID") ?? string.Empty;
        var url = AppleMusicPageParser.GetStringPath(item, "contentDescriptor", "url") ?? string.Empty;

        return new AppleMusicAlbum
        {
            Id = id,
            Name = name!,
            Url = url,
            ImageUrl = ResolveArtwork(item, "artwork"),
            Artists = ParseArtistLinks(item),
        };
    }

    private AppleMusicArtist? MapSearchArtist(JsonElement item)
    {
        var name = AppleMusicPageParser.GetString(item, "title");
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var id = AppleMusicPageParser.GetIdPath(item, "contentDescriptor", "identifiers", "storeAdamID") ?? string.Empty;
        var url = AppleMusicPageParser.GetStringPath(item, "contentDescriptor", "url") ?? string.Empty;

        return new AppleMusicArtist
        {
            Id = id,
            Name = name!,
            Url = url,
            ImageUrl = ResolveArtwork(item, "artwork"),
        };
    }

    private AppleMusicAlbum MapAlbumDetail(JsonElement item, string albumId, string albumUrl)
    {
        var name = AppleMusicPageParser.GetString(item, "title") ?? string.Empty;
        var about = AppleMusicPageParser.GetStringPath(item, "modalPresentationDescriptor", "paragraphText");
        var yearText = AppleMusicPageParser.GetString(item, "quaternaryTitle")
                       ?? AppleMusicPageParser.GetStringPath(item, "modalPresentationDescriptor", "headerSubtitle");

        return new AppleMusicAlbum
        {
            Id = AppleMusicPageParser.GetIdPath(item, "contentDescriptor", "identifiers", "storeAdamID") ?? albumId,
            Name = name,
            Url = AppleMusicPageParser.GetStringPath(item, "contentDescriptor", "url") ?? albumUrl,
            About = about,
            ReleaseDate = ParseYear(yearText),
            ImageUrl = ResolveArtwork(item, "artwork") ?? ResolveArtwork(item, "tallArtwork"),
            Artists = ParseArtistLinks(item),
        };
    }

    /// <summary>
    /// Reads the album track list out of the page data.
    /// The album page also carries recommendation shelves that use the very same item
    /// kind ("trackLockup"), so the section id is what tells the real list apart: only
    /// the album's own tracks live in a section whose id starts with "track-list".
    /// </summary>
    /// <param name="document">Parsed page data.</param>
    /// <returns>Track list, empty when the page has none.</returns>
    private static List<AppleMusicTrack> ParseTracks(JsonDocument document)
    {
        var tracks = new List<AppleMusicTrack>();

        foreach (var section in AppleMusicPageParser.GetSections(document))
        {
            var sectionId = AppleMusicPageParser.GetString(section, "id");
            if (sectionId is null || !sectionId.StartsWith("track-list", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var item in AppleMusicPageParser.GetItems(section))
            {
                var name = AppleMusicPageParser.GetString(item, "title");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                tracks.Add(new AppleMusicTrack
                {
                    Id = AppleMusicPageParser.GetIdPath(item, "contentDescriptor", "identifiers", "storeAdamID") ?? string.Empty,
                    Name = name!,
                    TrackNumber = AppleMusicPageParser.GetInt(item, "trackNumber"),
                    DiscNumber = AppleMusicPageParser.GetInt(item, "discNumber"),
                    DurationMs = AppleMusicPageParser.GetInt(item, "duration"),
                    ArtistName = AppleMusicPageParser.GetString(item, "artistName"),
                });
            }
        }

        return tracks;
    }

    private AppleMusicArtist MapArtistDetail(JsonElement item, string artistId, string artistUrl)
    {
        // Apple serves the localized artist biography in the "bio" field of the artist header.
        // It only exists for artists Apple wrote editorial content for.
        var bio = AppleMusicPageParser.GetString(item, "bio");

        return new AppleMusicArtist
        {
            Id = AppleMusicPageParser.GetIdPath(item, "contentDescriptor", "identifiers", "storeAdamID") ?? artistId,
            Name = AppleMusicPageParser.GetString(item, "title") ?? string.Empty,
            Url = AppleMusicPageParser.GetStringPath(item, "contentDescriptor", "url") ?? artistUrl,
            About = string.IsNullOrWhiteSpace(bio) ? null : bio!.Trim(),
            ImageUrl = ResolveArtwork(item, "circleArtwork")
                       ?? ResolveArtwork(item, "artistLogo")
                       ?? ResolveArtwork(item, "artwork"),

            // wideArtwork is the only genuinely landscape image Apple Music ships (2:1), and it
            // exists for some artists only - it is what backdrops are made of.
            WideImageUrl = ResolveArtwork(item, "wideArtwork", 2000, 1125),

            // A transparent artist logo, used for the Logo image type. It must stay png:
            // requesting the same image as jpg drops the transparency (6 KB against 37 KB).
            LogoUrl = ResolveArtwork(item, "artistLogo", 1000, 1000, "png"),
        };
    }

    private static List<AppleMusicArtist> ParseArtistLinks(JsonElement item)
    {
        var artists = new List<AppleMusicArtist>();
        var links = AppleMusicPageParser.GetPath(item, "subtitleLinks");

        if (links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                var name = CleanArtistName(AppleMusicPageParser.GetString(link, "title"));
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                artists.Add(new AppleMusicArtist
                {
                    Name = name,
                    Id = AppleMusicPageParser.GetIdPath(link, "segue", "destination", "contentDescriptor", "identifiers", "storeAdamID") ?? string.Empty,
                    Url = AppleMusicPageParser.GetStringPath(link, "segue", "destination", "contentDescriptor", "url") ?? string.Empty,
                });
            }
        }

        // Fall back to the plain subtitle when no linked artists are available.
        if (artists.Count == 0)
        {
            var subtitle = CleanArtistName(AppleMusicPageParser.GetString(item, "subtitle"));
            if (!string.IsNullOrEmpty(subtitle))
            {
                artists.Add(new AppleMusicArtist { Name = subtitle });
            }
        }

        return artists;
    }

    /// <summary>
    /// Removes the content type prefix that Apple Music sometimes puts in front of the
    /// artist name (for example "专辑 · Taylor Swift").
    /// </summary>
    /// <param name="name">Raw name.</param>
    /// <returns>Cleaned name.</returns>
    private static string CleanArtistName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        // Apple separates the parts with a middle dot surrounded by narrow no-break spaces.
        var text = name!.Replace('\u00a0', ' ').Replace('\u202f', ' ').Trim();
        var separator = text.IndexOf('·');
        if (separator <= 0)
        {
            return text;
        }

        var head = text.Substring(0, separator).Trim();
        foreach (var label in ContentTypeLabels)
        {
            if (string.Equals(head, label, StringComparison.OrdinalIgnoreCase))
            {
                return text.Substring(separator + 1).Trim();
            }
        }

        return text;
    }

    private static JsonElement? FindFirstItem(JsonDocument document, string itemKind, string idPrefix)
    {
        foreach (var section in AppleMusicPageParser.GetSections(document))
        {
            var kind = AppleMusicPageParser.GetString(section, "itemKind");
            var sectionId = AppleMusicPageParser.GetString(section, "id");
            var matches = string.Equals(kind, itemKind, StringComparison.OrdinalIgnoreCase)
                          || (sectionId is not null && sectionId.StartsWith(idPrefix, StringComparison.OrdinalIgnoreCase));

            if (!matches)
            {
                continue;
            }

            foreach (var item in AppleMusicPageParser.GetItems(section))
            {
                return item;
            }
        }

        return null;
    }

    private static string? GetFirstTitle(JsonElement item, string arrayName)
    {
        var array = AppleMusicPageParser.GetPath(item, arrayName);
        if (array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in array.EnumerateArray())
        {
            var title = AppleMusicPageParser.GetString(entry, "title");
            if (!string.IsNullOrEmpty(title))
            {
                return title;
            }
        }

        return null;
    }

    private static string? ResolveArtwork(JsonElement item, string propertyName, int width = 1200, int height = 1200, string format = "jpg")
    {
        var url = AppleMusicPageParser.GetStringPath(item, propertyName, "dictionary", "url");
        return string.IsNullOrEmpty(url) ? null : PluginUtils.ResolveArtworkUrl(url!, width, height, format);
    }

    private static DateTime? ParseYear(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = Regex.Match(text!, @"(19|20)\d{2}");
        if (!match.Success || !int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return null;
        }

        return new DateTime(year, 1, 1);
    }

    private async Task<string?> GetPageAsync(string url, CancellationToken cancellationToken)
    {
        _logger.Debug("Apple Music: opening page {0}", url);

        using var response = await _httpClient.GetResponse(new HttpRequestOptions
        {
            Url = url,
            CancellationToken = cancellationToken,
            BufferContent = true,
            EnableHttpCompression = true,
        }).ConfigureAwait(false);

        if (response.Content is null)
        {
            _logger.Warn("Apple Music: empty response for {0}", url);
            return null;
        }

        using var reader = new StreamReader(response.Content);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}
