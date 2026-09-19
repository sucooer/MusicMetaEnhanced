using System;
using System.Collections.Generic;
using System.IO;
using Emby.Plugin.AppleMusic.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugin.AppleMusic;

/// <summary>
/// The Apple Music metadata plugin for Emby.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Music Meta Enhanced";

    /// <inheritdoc />
    public override string Description => "Enhanced music metadata for Emby: Apple Music providers (multi-storefront), Netease Cloud Music biographies and image fallback, alias bridging and dashboard tools.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("2f9d4b6e-9c7a-4a1e-8f3b-6d5c2a7e1b40");

    /// <summary>
    /// Gets the plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "applemusic",
                DisplayName = "音乐元数据",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "audiotrack",
            },
        };
    }

    /// <inheritdoc />
    public ImageFormat ThumbImageFormat => ImageFormat.Jpg;

    /// <inheritdoc />
    public Stream GetThumbImage()
    {
        var stream = GetType().Assembly.GetManifestResourceStream(GetType().Namespace + ".Configuration.plugin.jpg");
        return stream ?? Stream.Null;
    }
}
