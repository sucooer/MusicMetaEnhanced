using System;
using MediaBrowser.Model.Providers;

namespace Emby.Plugin.MusicMetaEnhanced.Dtos;

/// <summary>
/// Common interface for Apple Music items.
/// </summary>
public interface IAppleMusicItem
{
    /// <summary>
    /// Gets or sets the Apple Music ID.
    /// </summary>
    string Id { get; set; }

    /// <summary>
    /// Gets or sets the item name.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets or sets the Apple Music web URL.
    /// </summary>
    string Url { get; set; }

    /// <summary>
    /// Gets or sets the image URL.
    /// </summary>
    string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    string? About { get; set; }

    /// <summary>
    /// Converts the item to an Emby remote search result.
    /// </summary>
    /// <returns>Remote search result.</returns>
    RemoteSearchResult ToRemoteSearchResult();

    /// <summary>
    /// Gets a value indicating whether the item carries any usable metadata.
    /// </summary>
    /// <returns>True if the item has metadata.</returns>
    bool HasMetadata();
}
