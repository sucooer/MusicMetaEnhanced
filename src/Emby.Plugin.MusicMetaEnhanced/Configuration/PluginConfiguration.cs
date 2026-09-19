using MediaBrowser.Model.Plugins;

namespace Emby.Plugin.MusicMetaEnhanced.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Apple Music storefront (country) used for searching and scraping.
    /// Two letter code as used by Apple Music, for example "us", "gb", "jp", "cn".
    /// </summary>
    public string Storefront { get; set; } = "cn";

    /// <summary>
    /// Gets or sets the storefront used for album metadata, for example "jp".
    /// Album names are kept in their original script everywhere, but artist names and
    /// some track titles are localized per storefront (奥華子 becomes "Hanako Oku" outside
    /// Japan). Japanese libraries get the original spellings from the Japanese storefront.
    /// Album data is read through the iTunes API, whose country parameter is not subject
    /// to the IP based geo redirect the web pages use. Empty follows the main storefront.
    /// </summary>
    public string AlbumStorefront { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the (experimental) Apple Music JSON API
    /// should be used instead of scraping the Apple Music website.
    /// </summary>
    public bool UseJsonSource { get; set; }

    /// <summary>
    /// Gets or sets the base URL of a NeteaseCloudMusicApi instance
    /// (https://github.com/NeteaseCloudMusicApiEnhanced/api-enhanced), for example
    /// "http://127.0.0.1:3000" for a local instance or a self hosted public one.
    /// Apple Music only ships biographies for major artists, so this is used to fill in
    /// Chinese artist biographies. Clear the value to disable the feature.
    /// </summary>
    public string NeteaseApiBaseUrl { get; set; } = MetadataSources.Netease.NeteaseMusicSource.DefaultApiBaseUrl;

    /// <summary>
    /// Gets or sets a value indicating whether a Netease Cloud Music biography should
    /// win over the Apple Music one. Apple Music biographies are usually localized too,
    /// so this defaults to true to satisfy "prefer Chinese" behaviour.
    /// </summary>
    public bool PreferNeteaseBio { get; set; } = true;
}
