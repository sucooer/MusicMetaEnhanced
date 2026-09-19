using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.AppleMusic.Dtos;

namespace Emby.Plugin.AppleMusic.MetadataSources;

/// <summary>
/// Item type handled by a metadata source.
/// </summary>
public enum ItemType
{
    /// <summary>
    /// Music album.
    /// </summary>
    Album,

    /// <summary>
    /// Music artist.
    /// </summary>
    Artist,
}

/// <summary>
/// Metadata source abstraction.
/// </summary>
public interface IMetadataSource
{
    /// <summary>
    /// Search Apple Music.
    /// </summary>
    /// <param name="searchTerm">Search term.</param>
    /// <param name="itemType">Item type to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of found items.</returns>
    Task<List<IAppleMusicItem>> SearchAsync(string searchTerm, ItemType itemType, CancellationToken cancellationToken);

    /// <summary>
    /// Get album data by Apple Music ID.
    /// </summary>
    /// <param name="albumId">Apple Music album ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Album data or null when not found.</returns>
    Task<AppleMusicAlbum?> GetAlbumAsync(string albumId, CancellationToken cancellationToken);

    /// <summary>
    /// Get artist data by Apple Music ID.
    /// </summary>
    /// <param name="artistId">Apple Music artist ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artist data or null when not found.</returns>
    Task<AppleMusicArtist?> GetArtistAsync(string artistId, CancellationToken cancellationToken);
}
