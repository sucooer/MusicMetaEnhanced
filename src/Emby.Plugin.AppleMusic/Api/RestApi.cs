using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.AppleMusic.MetadataSources;
using Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient;
using Emby.Plugin.AppleMusic.MetadataSources.Netease;
using Emby.Plugin.AppleMusic.Utils;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;

namespace Emby.Plugin.AppleMusic.Api;

/// <summary>
/// Saves the plugin configuration from the dashboard form. Every field is written
/// because the form always posts all of them.
/// </summary>
[Route("/AppleMusic/SaveConfig", "POST")]
public class SaveConfigRequest : IReturnVoid
{
    /// <summary>Artist / biography storefront.</summary>
    public string Storefront { get; set; } = string.Empty;

    /// <summary>Album storefront; empty follows the main storefront.</summary>
    public string AlbumStorefront { get; set; } = string.Empty;

    /// <summary>Netease API base URL; empty disables the feature.</summary>
    public string NeteaseApiBaseUrl { get; set; } = string.Empty;

    /// <summary>Whether the Netease biography wins over the Apple Music one.</summary>
    public bool PreferNeteaseBio { get; set; }

    /// <summary>Whether the experimental JSON source replaces the web source.</summary>
    public bool UseJsonSource { get; set; }
}

/// <summary>
/// Runs one of the manual operations from the dashboard page.
/// </summary>
[Route("/AppleMusic/Action", "GET")]
public class ActionRequest : IReturnVoid
{
    /// <summary>One of refresh-artists, refresh-albums, test-netease, test-apple.</summary>
    public string Op { get; set; } = string.Empty;

    /// <summary>"true" also overwrites values that are already present.</summary>
    public string Replace { get; set; } = string.Empty;
}

/// <summary>
/// Shows the current configuration and library statistics.
/// </summary>
[Route("/AppleMusic/Status", "GET")]
public class StatusRequest : IReturnVoid
{
}

/// <summary>
/// The dashboard endpoints of the plugin. Emby discovers <c>IService</c> implementations
/// on its own; the page itself cannot run JavaScript (Emby 4.9 does not execute inline
/// scripts in plugin pages), so every action is a plain link or a native form post.
/// </summary>
public class RestApi : IService, IRequiresRequest
{
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly IHttpClient _httpClient;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RestApi"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="fileSystem">Directory service.</param>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logManager">Log manager.</param>
    public RestApi(ILibraryManager libraryManager, IFileSystem fileSystem, IHttpClient httpClient, ILogManager logManager)
    {
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _httpClient = httpClient;
        _logger = logManager.GetLogger("Music Meta Enhanced");
    }

    /// <summary>
    /// Gets or sets the request context, set by the service framework.
    /// </summary>
    public IRequest Request { get; set; } = null!;

    /// <summary>
    /// Saves the configuration and returns to the configuration page.
    /// </summary>
    /// <param name="request">Posted form values.</param>
    public void Post(SaveConfigRequest request)
    {
        var config = Plugin.Instance!.Configuration!;
        config.Storefront = NormalizeStorefront(request.Storefront, "cn");
        config.AlbumStorefront = NormalizeStorefront(request.AlbumStorefront, string.Empty);
        config.NeteaseApiBaseUrl = (request.NeteaseApiBaseUrl ?? string.Empty).Trim();
        config.PreferNeteaseBio = request.PreferNeteaseBio;
        config.UseJsonSource = request.UseJsonSource;
        Plugin.Instance.SaveConfiguration();
        _logger.Info("Apple Music: configuration saved from the dashboard (storefront {0}, album storefront '{1}')", config.Storefront, config.AlbumStorefront);
        Request.Response.Redirect("../web/index.html#!/configurationpage?name=applemusic");
    }

    /// <summary>
    /// Runs a manual operation and renders a plain result page.
    /// </summary>
    /// <param name="request">Operation.</param>
    /// <returns>Task.</returns>
    public async Task Get(ActionRequest request)
    {
        switch (request.Op)
        {
            case "refresh-artists":
            {
                var replace = IsTrue(request.Replace);
                var count = CountItems("MusicArtist");
                StartBackgroundRefresh("MusicArtist", replace, count);
                await WriteHtmlAsync(
                    "已开始刷新艺人",
                    $"共找到 <b>{count}</b> 位艺人，元数据刷新已在后台开始（替换已有值：{(replace ? "是" : "否")}）。" +
                    "耗时取决于艺人数量，可在「日志」页观察进度。").ConfigureAwait(false);
                    break;
                }

            case "refresh-albums":
            {
                var replace = IsTrue(request.Replace);
                var count = CountItems("MusicAlbum");
                StartBackgroundRefresh("MusicAlbum", replace, count);
                await WriteHtmlAsync(
                    "已开始刷新专辑",
                    $"共找到 <b>{count}</b> 张专辑，元数据刷新已在后台开始（替换已有值：{(replace ? "是" : "否")}）。" +
                    "专辑按 AlbumStorefront 区域取数（当前：" + (PluginUtils.ConfiguredAlbumStorefront ?? "跟随主区域") + "）。").ConfigureAwait(false);
                    break;
                }

            case "test-netease":
            {
                var baseUrl = NeteaseMusicSource.ConfiguredBaseUrl;
                if (baseUrl is null)
                {
                    await WriteHtmlAsync("网易云 API 未启用", "配置里的网易云 API 地址为空，该功能处于关闭状态。").ConfigureAwait(false);
                    break;
                }

                var bio = await new NeteaseMusicSource(_logger, new SimpleHttpClient())
                    .GetArtistBioAsync("奥華子", CancellationToken.None).ConfigureAwait(false);
                await WriteHtmlAsync(
                    bio is null ? "网易云 API 不可用" : "网易云 API 正常",
                    bio is null
                        ? "未能取到测试艺人（奥華子）的简介，请检查地址与网络。"
                        : $"取到测试艺人（奥華子）的简介 {bio.Length} 字：<br/>{HtmlEncode(bio[..Math.Min(120, bio.Length)])}…").ConfigureAwait(false);
                break;
            }

            case "test-apple":
            {
                var source = MetadataSourceFactory.Create(_logger, _httpClient);
                var results = await source.SearchAsync("奥華子", ItemType.Artist, CancellationToken.None).ConfigureAwait(false);
                var first = results.Count > 0 ? results[0].Name : null;
                await WriteHtmlAsync(
                    results.Count > 0 ? "Apple Music 连通正常" : "Apple Music 无结果",
                    results.Count > 0
                        ? $"搜索「奥華子」返回 {results.Count} 条结果，第一条：{HtmlEncode(first ?? "?")}。" +
                          "若名字显示为本地化拼写（如 Hanako Oku），属于 Apple 该区的显示方式，不影响别名桥匹配。"
                        : "搜索「奥華子」没有返回结果，请检查 Storefront 与网络。").ConfigureAwait(false);
                break;
            }

            default:
                await WriteHtmlAsync("未知操作", HtmlEncode(request.Op)).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Renders the current configuration and a few library counts.
    /// </summary>
    /// <returns>Task.</returns>
    public async Task Get(StatusRequest request)
    {
        var config = Plugin.Instance!.Configuration!;
        var body =
            $"Storefront（艺人 / 简介）：<b>{HtmlEncode(PluginUtils.Storefront)}</b><br/>" +
            $"AlbumStorefront（专辑）：<b>{HtmlEncode(PluginUtils.ConfiguredAlbumStorefront ?? "（跟随主区域）")}</b><br/>" +
            $"网易云 API：<b>{HtmlEncode(string.IsNullOrWhiteSpace(config.NeteaseApiBaseUrl) ? "（未配置，功能关闭）" : config.NeteaseApiBaseUrl)}</b><br/>" +
            $"简介优先：<b>{(config.PreferNeteaseBio ? "中文（网易云）" : "Apple Music")}</b><br/>" +
            $"数据源：<b>{(config.UseJsonSource ? "JSON API（实验性）" : "网页抓取")}</b><br/><br/>" +
            $"库里艺人：<b>{CountItems("MusicArtist")}</b> 位，专辑：<b>{CountItems("MusicAlbum")}</b> 张";

        await WriteHtmlAsync("Apple Music 当前配置", body).ConfigureAwait(false);
    }


    private static string NormalizeStorefront(string value, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();
        return trimmed.Length == 0 ? fallback : trimmed;
    }

    private static bool IsTrue(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }

    private int CountItems(string itemType)
    {
        return _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { itemType },
            Recursive = true,
        }).Length;
    }

    private void StartBackgroundRefresh(string itemType, bool replaceAllMetadata, int count)
    {
        _logger.Info("Apple Music: background metadata refresh started for {0} item(s) of type {1} (replace: {2})", count, itemType, replaceAllMetadata);
        _ = Task.Run(async () =>
        {
            try
            {
                var items = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { itemType },
                    Recursive = true,
                });

                var options = new MetadataRefreshOptions(_fileSystem)
                {
                    MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                    ReplaceAllMetadata = replaceAllMetadata,
                    ForceSave = true,
                };

                var done = 0;
                foreach (var item in items)
                {
                    try
                    {
                        await item.RefreshMetadata(options, CancellationToken.None).ConfigureAwait(false);
                        done++;
                    }
                    catch (Exception exception)
                    {
                        _logger.ErrorException("Apple Music: refresh failed for '{0}'", exception, item.Name);
                    }
                }

                _logger.Info("Apple Music: background metadata refresh finished, {0}/{1} item(s) refreshed", done, items.Length);
            }
            catch (Exception exception)
            {
                _logger.ErrorException("Apple Music: background metadata refresh crashed", exception);
            }
        });
    }

    private async Task WriteHtmlAsync(string title, string bodyHtml)
    {
        var page = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>" + HtmlEncode(title) +
                   "</title></head><body style=\"font-family:sans-serif;max-width:36em;margin:4em auto;padding:0 1em;\"><h2>" +
                   HtmlEncode(title) + "</h2><p>" + bodyHtml +
                   "</p><p><a href=\"../web/index.html#!/configurationpage?name=applemusic\">返回 Apple Music 配置页</a></p></body></html>";

        var response = Request.Response;
        response.ContentType = "text/html; charset=utf-8";
        await response.OutputWriter.WriteAsync(Encoding.UTF8.GetBytes(page)).ConfigureAwait(false);
        await response.CompleteAsync().ConfigureAwait(false);
    }

    private static string HtmlEncode(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return s.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}
