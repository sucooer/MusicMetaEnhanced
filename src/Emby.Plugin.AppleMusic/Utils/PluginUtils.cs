using System;
using System.Globalization;
using System.Linq;

namespace Emby.Plugin.AppleMusic.Utils;

/// <summary>
/// Various general plugin utilities.
/// </summary>
public static class PluginUtils
{
    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public static string PluginName => "Music Meta Enhanced";

    /// <summary>
    /// Gets the configured Apple Music storefront.
    /// Falls back to "cn" because Apple Music redirects requests to the storefront of
    /// the caller's country - asking for another country from mainland China does not work.
    /// </summary>
    public static string Storefront
    {
        get
        {
            var configured = Plugin.Instance?.Configuration?.Storefront;
            return string.IsNullOrWhiteSpace(configured) ? "cn" : configured.Trim().ToLowerInvariant();
        }
    }

    /// <summary>
    /// Gets the Apple Music base URL for the configured storefront.
    /// </summary>
    public static string AppleMusicBaseUrl => $"https://music.apple.com/{Storefront}";

    /// <summary>
    /// Gets the storefront album metadata should come from, or null when albums follow
    /// the main storefront. See <see cref="Configuration.PluginConfiguration.AlbumStorefront"/>.
    /// </summary>
    public static string? ConfiguredAlbumStorefront
    {
        get
        {
            var configured = Plugin.Instance?.Configuration?.AlbumStorefront;
            return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim().ToLowerInvariant();
        }
    }

    /// <summary>
    /// Update image resolution (width)x(height)(opts) in an image URL.
    /// For example 1400x1400cc is an image with 1400x1400 resolution, center cropped.
    /// </summary>
    /// <param name="url">URL to work with.</param>
    /// <param name="newImageRes">New image resolution.</param>
    /// <param name="extension">File extension; artist logos must stay png to keep transparency.</param>
    /// <returns>Updated URL.</returns>
    public static string UpdateImageSize(string url, string newImageRes, string extension = "jpg")
    {
        var idx = url.LastIndexOf('/');
        if (idx < 0)
        {
            return url;
        }

        return string.Concat(url.AsSpan(0, idx + 1), newImageRes, ".", extension);
    }

    /// <summary>
    /// Get the Apple Music ID from an Apple Music URL.
    /// The URL format is always "https://music.apple.com/[storefront]/[item type]/[ID]".
    /// </summary>
    /// <param name="url">Apple Music URL.</param>
    /// <returns>Item ID.</returns>
    public static string GetIdFromUrl(string url)
    {
        return url.Split('/').LastOrDefault(string.Empty);
    }

    /// <summary>
    /// Resolve an Apple Music artwork URL template into an actual URL.
    /// The template uses {w}, {h}, {c} (crop code) and {f} (format) placeholders.
    /// </summary>
    /// <param name="templateUrl">URL template from Apple Music.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="format">Image format.</param>
    /// <param name="crop">Crop code, for example "bb" or "sr".</param>
    /// <returns>Resolved URL.</returns>
    public static string ResolveArtworkUrl(string templateUrl, int width = 1200, int height = 1200, string format = "jpg", string crop = "bb")
    {
        return templateUrl
            .Replace("{w}", width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{h}", height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{c}", crop, StringComparison.Ordinal)
            .Replace("{f}", format, StringComparison.Ordinal);
    }
}
