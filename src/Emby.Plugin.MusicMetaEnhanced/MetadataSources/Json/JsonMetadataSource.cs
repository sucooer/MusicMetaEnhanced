using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MusicMetaEnhanced.Dtos;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient.Models.Requests;
using Emby.Plugin.MusicMetaEnhanced.Utils;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json;

/// <summary>
/// Apple Music JSON metadata source.
/// This source retrieves metadata from the Apple Music JSON API.
/// </summary>
public class JsonMetadataSource : IMetadataSource
{
    private readonly DefaultApiClient _apiClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMetadataSource"/> class.
    /// </summary>
    /// <param name="apiClient">API client instance.</param>
    /// <param name="logger">Logger instance.</param>
    public JsonMetadataSource(DefaultApiClient apiClient, ILogger logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<IAppleMusicItem>> SearchAsync(string searchTerm, ItemType itemType, CancellationToken cancellationToken)
    {
        _logger.Info("Apple Music: searching for {0} with term: {1}", itemType, searchTerm);

        var request = new SearchRequest
        {
            Term = searchTerm,
            Limit = 25,
            Types = itemType switch
            {
                ItemType.Album => new[] { "albums" },
                ItemType.Artist => new[] { "artists" },
                _ => new[] { "albums", "artists" },
            },
        };

        var response = await _apiClient.SearchAsync(request, cancellationToken).ConfigureAwait(false);

        return itemType switch
        {
            ItemType.Album => ConvertAlbums(response.Albums?.Data ?? new List<SearchResult>()).Cast<IAppleMusicItem>().ToList(),
            ItemType.Artist => ConvertArtists(response.Artists?.Data ?? new List<SearchResult>()).Cast<IAppleMusicItem>().ToList(),
            _ => new List<IAppleMusicItem>(),
        };
    }

    /// <inheritdoc />
    public async Task<AppleMusicAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken)
    {
        var result = await _apiClient.GetAlbumAsync(albumId, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            _logger.Warn("Apple Music: album not found: {0}", albumId);
            return null;
        }

        return ConvertAlbums(new[] { result }).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<AppleMusicArtist?> GetArtistAsync(string artistId, CancellationToken cancellationToken)
    {
        var result = await _apiClient.GetArtistAsync(artistId, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            _logger.Warn("Apple Music: artist not found: {0}", artistId);
            return null;
        }

        return ConvertArtists(new[] { result }).FirstOrDefault();
    }

    private List<AppleMusicAlbum> ConvertAlbums(IEnumerable<SearchResult> results)
    {
        return results.Select(r => new AppleMusicAlbum
        {
            Id = r.Id,
            Name = r.Attributes?.Name ?? string.Empty,
            Url = r.Attributes?.Url ?? string.Empty,
            ImageUrl = ResolveArtworkUrl(r.Attributes?.Artwork?.Url),
            About = r.Attributes?.EditorialNotes?.Standard ?? r.Attributes?.EditorialNotes?.Short,
            ReleaseDate = ParseReleaseDate(r.Attributes?.ReleaseDate),
            Artists = ConvertArtistsFromRelationships(r.Relationships),
        }).ToList();
    }

    private List<AppleMusicArtist> ConvertArtists(IEnumerable<SearchResult> results)
    {
        return results.Select(r => new AppleMusicArtist
        {
            Id = r.Id,
            Name = r.Attributes?.Name ?? string.Empty,
            Url = r.Attributes?.Url ?? string.Empty,
            ImageUrl = ResolveArtworkUrl(r.Attributes?.Artwork?.Url),
            About = r.Attributes?.EditorialNotes?.Standard ?? r.Attributes?.EditorialNotes?.Short,
        }).ToList();
    }

    private List<AppleMusicArtist> ConvertArtistsFromRelationships(Relationships? relationships)
    {
        if (relationships?.Artists?.Data is null)
        {
            return new List<AppleMusicArtist>();
        }

        return ConvertArtists(relationships.Artists.Data);
    }

    private static string? ResolveArtworkUrl(string? templateUrl)
    {
        return string.IsNullOrEmpty(templateUrl) ? null : PluginUtils.ResolveArtworkUrl(templateUrl);
    }

    private static DateTime? ParseReleaseDate(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
        {
            return null;
        }

        return DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }
}
