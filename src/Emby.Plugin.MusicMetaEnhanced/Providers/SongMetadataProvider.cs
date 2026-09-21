using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.MusicMetaEnhanced.Dtos;
using Emby.Plugin.MusicMetaEnhanced.ExternalIds;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Itunes;
using Emby.Plugin.MusicMetaEnhanced.MetadataSources.Json.ApiClient;
using Emby.Plugin.MusicMetaEnhanced.Utils;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.MusicMetaEnhanced.Providers;

/// <summary>
/// Apple Music song (track) metadata provider.
/// Emby fills songs from their embedded tags, which strm files do not have - those keep
/// showing the file name forever. This provider resolves a song through its album on
/// Apple Music instead, which works without any local tag.
/// </summary>
public class SongMetadataProvider : IRemoteMetadataProvider<Audio, SongInfo>, IHasOrder, IHasSupportedExternalIdentifiers
{
    /// <summary>
    /// How many album candidates are tried before giving up. A song is matched by its
    /// track number or title, so checking the best few hits is enough.
    /// </summary>
    private const int MaxAlbumCandidates = 3;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Leading track numbers Emby keeps in a name when a file has no tags, e.g. "02. Song".
    /// </summary>
    private static readonly Regex LeadingTrackNumber = new(@"^\s*\d{1,3}\s*[.,、\-–]\s*", RegexOptions.Compiled);

    /// <summary>
    /// Album track lists, shared by all songs of the same album. Refreshing an album
    /// refreshes every song, and each one would otherwise re-request the same album.
    /// </summary>
    private static readonly ConcurrentDictionary<string, CacheEntry> TrackCache = new(StringComparer.Ordinal);

    private readonly ILogger _logger;
    private readonly IMetadataSource _metadataSource;
    private readonly ItunesAlbumSource _itunes;

    /// <summary>
    /// Initializes a new instance of the <see cref="SongMetadataProvider"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logger">Logger.</param>
    public SongMetadataProvider(IHttpClient httpClient, ILogger logger)
    {
        _logger = logger;
        _metadataSource = MetadataSourceFactory.Create(logger, httpClient);
        _itunes = new ItunesAlbumSource(new SimpleHttpClient(), logger);
    }

    /// <inheritdoc />
    public string Name => PluginUtils.PluginName;

    /// <summary>
    /// Gets the provider order, matching the album provider so a song is resolved by the
    /// same source as its album.
    /// </summary>
    public int Order => -5;

    /// <inheritdoc />
    public string[] GetSupportedExternalIdentifiers()
    {
        return new[] { ProviderKey.AppleMusicSong };
    }

    /// <inheritdoc />
    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SongInfo searchInfo, CancellationToken cancellationToken)
    {
        // Emby has no identify dialog for songs (the remote search endpoint for Audio
        // does not even exist), so there is nothing to search for.
        return Task.FromResult<IEnumerable<RemoteSearchResult>>(Array.Empty<RemoteSearchResult>());
    }

    /// <inheritdoc />
    public async Task<MetadataResult<Audio>> GetMetadata(SongInfo info, CancellationToken cancellationToken)
    {
        // Emby groups songs into an album by the Album and AlbumArtists fields. A
        // "replace all metadata" refresh clears them before providers run, so they have
        // to be rebuilt here or the song silently drops out of its album.
        var albumName = FirstNonEmpty(info.Album, DirectoryNameOf(info.Path));
        var albumArtists = info.AlbumArtists?.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray() ?? Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(albumName))
        {
            _logger.Info("Apple Music: song '{0}' has no album name or folder, cannot resolve it", info.Name);
            return EmptyResult();
        }

        var storefront = PluginUtils.ConfiguredAlbumStorefront ?? PluginUtils.Storefront;

        // A song carrying its own Apple Music id is resolved directly. This is the manual
        // fallback: paste a track id into a song and it no longer depends on its album
        // being found.
        var songId = info.GetProviderId(ProviderKey.AppleMusicSong);
        if (!string.IsNullOrEmpty(songId))
        {
            var direct = await _itunes.LookupTrackAsync(songId!, storefront, cancellationToken).ConfigureAwait(false);
            if (direct is not null)
            {
                _logger.Info("Apple Music: song '{0}' resolved from its own track id {1}", info.Name, songId);
                return ToResult(direct.Name, direct.TrackNumber, direct.DiscNumber, direct.ArtistName, direct.Id, albumName, AlbumArtistsOr(albumArtists, direct.ArtistName));
            }
        }

        var candidates = await FindAlbumCandidatesAsync(info, storefront, cancellationToken).ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            var tracks = await GetAlbumTracksAsync(candidate.Id, storefront, cancellationToken).ConfigureAwait(false);
            if (tracks.Count == 0)
            {
                continue;
            }

            var track = MatchTrack(tracks, info);
            if (track is null)
            {
                continue;
            }

            _logger.Info("Apple Music: song '{0}' matched track '{1}' of album {2}", info.Name, track.Name, candidate.Id);
            return ToResult(
                track.Name,
                track.TrackNumber,
                track.DiscNumber,
                track.ArtistName ?? candidate.ArtistName,
                track.Id,
                albumName,
                AlbumArtistsOr(albumArtists, candidate.ArtistName));
        }

        _logger.Info("Apple Music: no Apple Music track matches song '{0}' of album '{1}'", info.Name, albumName);
        return EmptyResult();
    }

    /// <inheritdoc />
    public Task<HttpResponseInfo> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return Task.FromResult<HttpResponseInfo>(null!);
    }

    /// <summary>
    /// Collects the albums worth checking for this song, best hit first.
    /// </summary>
    /// <param name="info">Lookup info.</param>
    /// <param name="storefront">Storefront code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album candidates.</returns>
    private async Task<List<Candidate>> FindAlbumCandidatesAsync(SongInfo info, string storefront, CancellationToken cancellationToken)
    {
        var term = BuildSearchTerm(info);
        var candidates = new List<Candidate>();

        var results = await _itunes.SearchAsync(term, storefront, cancellationToken).ConfigureAwait(false);

        // The local album name can differ from the Apple one (Japanese releases are often
        // listed under an English title), so the title only sorts the candidates - whether
        // an album really is the right one is decided by its track list.
        candidates.AddRange(results
            .Where(album => !string.IsNullOrEmpty(album.Id))
            .OrderBy(album => TitleMatcher.IsSameTitle(album.Name, info.Album) ? 0 : 1)
            .Take(MaxAlbumCandidates)
            .Select(album => new Candidate(album.Id, album.Name, album.ArtistName)));

        if (candidates.Count == 0)
        {
            var webResults = await _metadataSource.SearchAsync(term, ItemType.Album, cancellationToken).ConfigureAwait(false);
            candidates.AddRange(webResults
                .OfType<AppleMusicAlbum>()
                .Where(album => !string.IsNullOrEmpty(album.Id))
                .Take(MaxAlbumCandidates)
                .Select(album => new Candidate(album.Id, album.Name, album.Artists.FirstOrDefault()?.Name)));
        }

        return candidates;
    }

    /// <summary>
    /// Gets the tracks of an album, from the iTunes API when it ships them and from the
    /// Apple Music web page otherwise. Results are cached for a short while.
    /// </summary>
    /// <param name="albumId">Album Adam id.</param>
    /// <param name="storefront">Storefront code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Track list, possibly empty.</returns>
    private async Task<IReadOnlyList<AppleMusicTrack>> GetAlbumTracksAsync(string albumId, string storefront, CancellationToken cancellationToken)
    {
        var key = storefront + "/" + albumId;

        if (TrackCache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Tracks;
        }

        var tracks = new List<AppleMusicTrack>();

        var itunesTracks = await _itunes.GetTracksAsync(albumId, storefront, cancellationToken).ConfigureAwait(false);
        if (itunesTracks.Count > 0)
        {
            tracks.AddRange(itunesTracks.Select(track => new AppleMusicTrack
            {
                Id = track.Id,
                Name = track.Name,
                TrackNumber = track.TrackNumber,
                DiscNumber = track.DiscNumber,
                DurationMs = track.DurationMs,
                ArtistName = track.ArtistName,
            }));
        }
        else
        {
            var album = await _metadataSource.GetAlbumAsync(albumId, cancellationToken).ConfigureAwait(false);
            if (album?.Tracks is not null)
            {
                tracks.AddRange(album.Tracks);
            }
        }

        TrackCache[key] = new CacheEntry { Tracks = tracks, ExpiresAt = DateTimeOffset.UtcNow.Add(CacheTtl) };
        return tracks;
    }

    /// <summary>
    /// Finds the track that belongs to this song.
    /// </summary>
    /// <param name="tracks">Album track list.</param>
    /// <param name="info">Lookup info.</param>
    /// <returns>Matching track, or null when there is none.</returns>
    private static AppleMusicTrack? MatchTrack(IReadOnlyList<AppleMusicTrack> tracks, SongInfo info)
    {
        // The track number is the reliable signal: it comes from the file name or the tag
        // and does not depend on which script the storefront uses.
        if (info.IndexNumber.HasValue)
        {
            var byNumber = tracks.FirstOrDefault(track =>
                track.TrackNumber == info.IndexNumber
                && (!info.ParentIndexNumber.HasValue || track.DiscNumber is null || track.DiscNumber == info.ParentIndexNumber));

            if (byNumber is not null)
            {
                return byNumber;
            }
        }

        var localName = StripLeadingTrackNumber(info.Name);
        if (string.IsNullOrEmpty(localName))
        {
            return null;
        }

        return tracks.FirstOrDefault(track => TitleMatcher.IsSameTitle(track.Name, localName));
    }

    /// <summary>
    /// Builds the search term used to find the album a song belongs to.
    /// </summary>
    /// <param name="info">Lookup info.</param>
    /// <returns>Search term.</returns>
    private static string BuildSearchTerm(SongInfo info)
    {
        var albumArtist = info.AlbumArtists?.FirstOrDefault() ?? string.Empty;
        var album = FirstNonEmpty(info.Album, DirectoryNameOf(info.Path));
        return string.IsNullOrEmpty(albumArtist) ? album : albumArtist + " " + album;
    }

    /// <summary>
    /// Removes a leading track number from an untagged file name, e.g. "02. Song" or "3 - Song".
    /// </summary>
    /// <param name="name">Item name.</param>
    /// <returns>Name without the number prefix.</returns>
    private static string StripLeadingTrackNumber(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        return LeadingTrackNumber.Replace(name!, string.Empty).Trim();
    }

    private static MetadataResult<Audio> ToResult(
        string name,
        int? trackNumber,
        int? discNumber,
        string? artistName,
        string? trackId,
        string albumName,
        string[] albumArtists)
    {
        var item = new Audio
        {
            Name = name,
            IndexNumber = trackNumber,
            ParentIndexNumber = discNumber,

            // Both fields keep the song inside its album; see GetMetadata.
            Album = albumName,
            AlbumArtists = albumArtists,
            Artists = albumArtists.Length != 0 ? albumArtists : Array.Empty<string>(),
        };

        if (!string.IsNullOrEmpty(artistName))
        {
            item.Artists = new[] { artistName! };
        }

        var result = new MetadataResult<Audio> { Item = item, HasMetadata = true };

        if (!string.IsNullOrEmpty(trackId))
        {
            result.Item.SetProviderId(ProviderKey.AppleMusicSong, trackId!);
        }

        return result;
    }

    private static string[] AlbumArtistsOr(string[] albumArtists, string? fallback)
    {
        if (albumArtists.Length != 0)
        {
            return albumArtists;
        }

        return string.IsNullOrWhiteSpace(fallback) ? Array.Empty<string>() : new[] { fallback! };
    }

    private static string DirectoryNameOf(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFileName(Path.GetDirectoryName(path!)) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!;
            }
        }

        return string.Empty;
    }

    private static MetadataResult<Audio> EmptyResult()
    {
        return new MetadataResult<Audio> { HasMetadata = false };
    }

    private sealed record Candidate(string Id, string Name, string? ArtistName);

    private sealed class CacheEntry
    {
        /// <summary>
        /// Gets or sets the cached tracks.
        /// </summary>
        public IReadOnlyList<AppleMusicTrack> Tracks { get; set; } = Array.Empty<AppleMusicTrack>();

        /// <summary>
        /// Gets or sets the expiry time.
        /// </summary>
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
