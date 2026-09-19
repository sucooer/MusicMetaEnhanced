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
/// <remarks>
/// Emby's auth token lives in the web client's memory and is sent as a request header.
/// A native form post cannot attach headers, so these dashboard endpoints must be
/// marked unauthenticated; <see cref="RestApi.IsCrossSiteRequest"/> rejects foreign
/// origins instead.
/// </remarks>
[Route("/AppleMusic/SaveConfig", "POST")]
[Unauthenticated]
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
[Unauthenticated]
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
[Unauthenticated]
public class StatusRequest : IReturnVoid
{
}

/// <summary>
/// The dashboard configuration form, rendered dynamically so the saved values are
/// visible. The embedded static page embeds this in an iframe.
/// </summary>
[Route("/AppleMusic/FormPage", "GET")]
[Unauthenticated]
public class FormPageRequest : IReturnVoid
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
    public async Task Post(SaveConfigRequest request)
    {
        if (IsCrossSiteRequest())
        {
            await WriteHtmlAsync("拒绝访问", "检测到跨站请求，已拒绝。请从 Emby 控制台的配置页发起操作。").ConfigureAwait(false);
            return;
        }

        var config = Plugin.Instance!.Configuration!;
        config.Storefront = NormalizeStorefront(request.Storefront, "cn");
        config.AlbumStorefront = NormalizeStorefront(request.AlbumStorefront, string.Empty);
        config.NeteaseApiBaseUrl = (request.NeteaseApiBaseUrl ?? string.Empty).Trim();
        config.PreferNeteaseBio = request.PreferNeteaseBio;
        config.UseJsonSource = request.UseJsonSource;
        Plugin.Instance.SaveConfiguration();
        _logger.Info("Apple Music: configuration saved from the dashboard (storefront {0}, album storefront '{1}')", config.Storefront, config.AlbumStorefront);
        Request.Response.Redirect("/emby/AppleMusic/FormPage");
    }

    /// <summary>
    /// Runs a manual operation and renders a plain result page.
    /// </summary>
    /// <param name="request">Operation.</param>
    /// <returns>Task.</returns>
    public async Task Get(ActionRequest request)
    {
        if (IsCrossSiteRequest())
        {
            await WriteHtmlAsync("拒绝访问", "检测到跨站请求，已拒绝。请从 Emby 控制台的配置页发起操作。").ConfigureAwait(false);
            return;
        }

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
    /// Renders the dashboard configuration form with the saved values filled in.
    /// </summary>
    /// <returns>Task.</returns>
    public async Task Get(FormPageRequest request)
    {
        if (IsCrossSiteRequest())
        {
            await WriteHtmlAsync("拒绝访问", "检测到跨站请求，已拒绝。请从 Emby 控制台的配置页发起操作。").ConfigureAwait(false);
            return;
        }

        var config = Plugin.Instance!.Configuration!;
        var storefront = HtmlEncode(PluginUtils.Storefront);
        var albumStorefront = HtmlEncode(PluginUtils.ConfiguredAlbumStorefront ?? string.Empty);
        var neteaseUrl = HtmlEncode(config.NeteaseApiBaseUrl ?? string.Empty);

        static string Option(string value, string label, string current)
        {
            var selected = string.Equals(value, current, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
            return $"<option value=\"{value}\"{selected}>{label}</option>";
        }

        var html = $"""
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"/><title>音乐元数据</title></head>
<body style="margin:0;background:#fff;color:#222;font-family:inherit;">
<div style="padding:1.5em 2em;box-sizing:border-box;">
<div style="font-size:1.2em;font-weight:600;margin-bottom:0.6em;">国家 / 地区</div>

<form method="post" action="/emby/AppleMusic/SaveConfig">
<label style="display:block;font-weight:600;margin:0.9em 0 0.25em;">Storefront（艺人 / 简介数据源）</label>
<select name="Storefront" style="width:100%;max-width:34em;box-sizing:border-box;padding:0.5em 0.6em;border:1px solid #bbb;border-radius:4px;font-size:14px;background:#fff;color:#222;">
{Option("cn", "cn — 中国大陆", storefront)}
{Option("jp", "jp — 日本", storefront)}
{Option("us", "us — 美国", storefront)}
{Option("hk", "hk — 中国香港", storefront)}
{Option("tw", "tw — 中国台湾", storefront)}
</select>
<p style="color:#666;font-size:13px;margin:0.3em 0 0;">Apple 会按服务器出口 IP 做地理跳转，网页数据源实际能用的区域有限，国内一般保持 cn。</p>

<label style="display:block;font-weight:600;margin:0.9em 0 0.25em;">AlbumStorefront（专辑元数据专用区域）</label>
<select name="AlbumStorefront" style="width:100%;max-width:34em;box-sizing:border-box;padding:0.5em 0.6em;border:1px solid #bbb;border-radius:4px;font-size:14px;background:#fff;color:#222;">
{Option("", "（跟随上面的 Storefront）", albumStorefront)}
{Option("jp", "jp — 日本（假名原文，推荐日语库）", albumStorefront)}
{Option("cn", "cn — 中国大陆", albumStorefront)}
{Option("us", "us — 美国", albumStorefront)}
{Option("hk", "hk — 中国香港", albumStorefront)}
{Option("tw", "tw — 中国台湾", albumStorefront)}
</select>
<p style="color:#666;font-size:13px;margin:0.3em 0 0;">专辑数据走 iTunes API，这个区域不受 IP 跳转影响，jp 区返回原始假名的艺人名与曲目名。</p>

<label style="display:block;font-weight:600;margin:0.9em 0 0.25em;">网易云 API 地址（中文简介来源，留空关闭）</label>
<input name="NeteaseApiBaseUrl" type="text" autocomplete="off" value="{neteaseUrl}" style="width:100%;max-width:34em;box-sizing:border-box;padding:0.5em 0.6em;border:1px solid #bbb;border-radius:4px;font-size:14px;background:#fff;color:#222;" />
<p style="color:#666;font-size:13px;margin:0.3em 0 0;">Apple 只给少数艺人配简介，网易云补中文简介；也用于艺人头像回退与别名匹配。</p>

<label style="display:block;font-weight:600;margin:0.9em 0 0.25em;">简介优先级</label>
<select name="PreferNeteaseBio" style="width:100%;max-width:34em;box-sizing:border-box;padding:0.5em 0.6em;border:1px solid #bbb;border-radius:4px;font-size:14px;background:#fff;color:#222;">
{Option("true", "优先中文（网易云）", config.PreferNeteaseBio ? "true" : "false")}
{Option("false", "优先 Apple Music", config.PreferNeteaseBio ? "true" : "false")}
</select>

<label style="display:block;font-weight:600;margin:0.9em 0 0.25em;">数据源</label>
<select name="UseJsonSource" style="width:100%;max-width:34em;box-sizing:border-box;padding:0.5em 0.6em;border:1px solid #bbb;border-radius:4px;font-size:14px;background:#fff;color:#222;">
{Option("false", "网页抓取（默认，稳定）", config.UseJsonSource ? "true" : "false")}
{Option("true", "JSON API（实验性）", config.UseJsonSource ? "true" : "false")}
</select>

<div style="margin-top:1.2em;">
<button type="submit" style="display:inline-block;vertical-align:top;padding:0.6em 1.2em;border:0;border-radius:4px;background:#00a4dc;color:#fff;font-size:14px;line-height:1.5;font-family:inherit;cursor:pointer;">保存配置</button>
<a href="/emby/AppleMusic/Status" target="_blank" rel="noopener" style="display:inline-block;vertical-align:top;padding:0.6em 1.2em;border-radius:4px;background:#777;color:#fff !important;font-size:14px;line-height:1.5;text-decoration:none !important;">查看当前配置与状态</a>
</div>
</form>

<div style="font-size:1.2em;font-weight:600;margin:1.6em 0 0.6em;">手动操作</div>
<p style="color:#666;font-size:13px;margin:0 0 0.6em;">刷新在服务器后台执行，点击后可离开此页；大量条目时请耐心等待完成后查看结果。</p>

<div>
<a href="/emby/AppleMusic/Action?op=refresh-artists" class="am-btn-green" style="display:inline-block;vertical-align:top;margin:0 0.5em 0.5em 0;padding:0.6em 1.2em;border-radius:4px;background:#4caf50;color:#fff !important;font-size:14px;line-height:1.5;text-decoration:none !important;">刷新全部艺人元数据</a>
<a href="/emby/AppleMusic/Action?op=refresh-artists&amp;replace=true" style="display:inline-block;vertical-align:top;margin:0 0.5em 0.5em 0;padding:0.6em 1.2em;border-radius:4px;background:#00a4dc;color:#fff !important;font-size:14px;line-height:1.5;text-decoration:none !important;">刷新全部艺人（替换已有值）</a>
</div>
<p style="color:#666;font-size:13px;margin:0 0 0.6em;">按名字匹配补全缺失的简介 / Apple Music ID；「替换已有值」会同时覆盖已有简介并把外部 ID 清空重写。</p>

<div>
<a href="/emby/AppleMusic/Action?op=refresh-albums" style="display:inline-block;vertical-align:top;margin:0 0.5em 0.5em 0;padding:0.6em 1.2em;border-radius:4px;background:#4caf50;color:#fff !important;font-size:14px;line-height:1.5;text-decoration:none !important;">刷新全部专辑元数据</a>
<a href="/emby/AppleMusic/Action?op=refresh-albums&amp;replace=true" style="display:inline-block;vertical-align:top;margin:0 0.5em 0.5em 0;padding:0.6em 1.2em;border-radius:4px;background:#00a4dc;color:#fff !important;font-size:14px;line-height:1.5;text-decoration:none !important;">刷新全部专辑（替换已有值）</a>
</div>
<p style="color:#666;font-size:13px;margin:0 0 0.6em;">用当前 AlbumStorefront 区域补全专辑名 / 年份 / 流派 / Apple Music ID。</p>

<div>
<a href="/emby/AppleMusic/Action?op=test-netease" style="display:inline-block;vertical-align:top;margin:0 0.5em 0.5em 0;padding:0.6em 1.2em;border-radius:4px;background:#777;color:#fff !important;font-size:14px;line-height:1.5;text-decoration:none !important;">测试网易云 API 连通性</a>
<a href="/emby/AppleMusic/Action?op=test-apple" style="display:inline-block;vertical-align:top;margin:0 0.5em 0.5em 0;padding:0.6em 1.2em;border-radius:4px;background:#777;color:#fff !important;font-size:14px;line-height:1.5;text-decoration:none !important;">测试 Apple Music 连通性</a>
</div>

</div>
""";
        html += "<script>window.addEventListener('load',function(){try{window.frameElement.style.height=document.documentElement.scrollHeight+'px';}catch(e){}});</" + "script></body></html>";

        var response = Request.Response;
        response.ContentType = "text/html; charset=utf-8";
        await response.OutputWriter.WriteAsync(Encoding.UTF8.GetBytes(html)).ConfigureAwait(false);
        await response.CompleteAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Renders the current configuration and a few library counts.
    /// </summary>
    /// <returns>Task.</returns>
    public async Task Get(StatusRequest request)
    {
        if (IsCrossSiteRequest())
        {
            await WriteHtmlAsync("拒绝访问", "检测到跨站请求，已拒绝。请从 Emby 控制台的配置页发起操作。").ConfigureAwait(false);
            return;
        }

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

    /// <summary>
    /// These endpoints accept unauthenticated native form posts and link navigations
    /// (Emby's auth token cannot be attached to either), so the one real protection is
    /// rejecting requests whose Referer points at a different host - the signature of a
    /// cross-site forgery. Typing the URL directly (no Referer at all) still works, and
    /// api_key keeps working for scripted use.
    /// </summary>
    private bool IsCrossSiteRequest()
    {
        var referer = Request.Headers["Referer"];
        if (string.IsNullOrEmpty(referer))
        {
            return false;
        }

        var host = Request.Headers["Host"];
        return !string.IsNullOrEmpty(host) && !referer.Contains(host, StringComparison.OrdinalIgnoreCase);
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
        // This page is shown inside the dashboard iframe, so it must paint its own
        // opaque background (otherwise the dashboard bleeds through) and shrink the
        // iframe to the content height (otherwise the fixed iframe height leaves a
        // huge scrollable void). The resize script runs fine here: the "no inline
        // scripts" limitation only applies to the static plugin configuration page,
        // not to pages the plugin serves itself.
        var page = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>" + HtmlEncode(title) +
                   "</title><style>html,body{margin:0;padding:0;background:#fff;color:#222;font-family:sans-serif;}" +
                   ".wrap{max-width:36em;margin:0 auto;padding:2.5em 1.5em;}h2{font-size:1.3em;margin-top:0;}" +
                   "a{color:#00a4dc;}</style></head><body><div class=\"wrap\"><h2>" +
                   HtmlEncode(title) + "</h2><p>" + bodyHtml +
                   "</p><p><a href=\"/emby/AppleMusic/FormPage\">返回音乐元数据配置页</a></p></div>" +
                   "<script>window.addEventListener('load',function(){try{window.frameElement.style.height=document.documentElement.scrollHeight+'px';}catch(e){}});</" + "script></body></html>";

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
