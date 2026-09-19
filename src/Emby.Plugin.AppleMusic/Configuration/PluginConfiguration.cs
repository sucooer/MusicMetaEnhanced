using MediaBrowser.Model.Plugins;

namespace Emby.Plugin.AppleMusic.Configuration;

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
    /// Gets or sets a value indicating whether the (experimental) Apple Music JSON API
    /// should be used instead of scraping the Apple Music website.
    /// </summary>
    public bool UseJsonSource { get; set; }

    /// <summary>
    /// Gets or sets the base URL of a local NeteaseCloudMusicApi instance
    /// (for example "http://127.0.0.1:3000"). Apple Music only ships biographies for
    /// major artists, so this is used to fill in Chinese artist biographies.
    /// Leave empty to disable.
    /// </summary>
    public string NeteaseApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether a Netease Cloud Music biography should
    /// win over the Apple Music one. Apple Music biographies are usually localized too,
    /// so this defaults to true to satisfy "prefer Chinese" behaviour.
    /// </summary>
    public bool PreferNeteaseBio { get; set; } = true;
}
