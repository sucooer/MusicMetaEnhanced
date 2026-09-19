using System.Text;
using Emby.Plugin.AppleMusic.Dtos;
using Emby.Plugin.AppleMusic.MetadataSources;
using Emby.Plugin.AppleMusic.MetadataSources.Web;
using Emby.Plugin.AppleMusic.Utils;
using MediaBrowser.Model.Logging;
using ScrapeTest;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length > 0 && args[0] == "--match" && args.Length > 1)
{
    // args[1] is a UTF-8 file with one artist name per line.
    var names = File.ReadAllLines(args[1]).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    var matchLogger = new ConsoleLogger();
    var matchSource = new WebMetadataSource(matchLogger, new HttpClientStub());

    Console.WriteLine($"{"库里名字",-24} {"结果",-6} {"苹果返回的名字",-26} {"id",-12} 匹配方式");
    var hit = 0;
    foreach (var name in names)
    {
        var results = await matchSource.SearchAsync(name, ItemType.Artist, CancellationToken.None);
        var candidates = results.OfType<AppleMusicArtist>().ToList();
        var match = candidates.FirstOrDefault(a => TitleMatcher.IsSameTitle(a.Name, name));
        if (match is not null)
        {
            hit++;
        }

        var how = match is null
            ? "-"
            : TitleMatcher.IsSameLiteralTitle(match.Name, name) ? "字面相同" : "假名转罗马字";
        Console.WriteLine(
            $"{name,-24} {(match is null ? "未命中" : "命中"),-6} {match?.Name ?? candidates.FirstOrDefault()?.Name ?? "(无结果)",-26} {match?.Id,-12} {how}");
    }

    Console.WriteLine($"命中 {hit}/{names.Count}");
    return 0;
}

if (args.Length > 0 && args[0] == "--normalize")
{
    Console.WriteLine("TitleMatcher normalization check");
    Console.WriteLine();

    var pairs = args.Length > 1
        ? new[] { (args[1], args.Length > 2 ? args[2] : string.Empty) }
        : new[]
        {
            ("とた", "Tota"),
            ("とた", "totâ"),
            ("とた", "Pete Townshend"),
            ("緑黄色社会", "緑黄色社会"),
            ("緑黄色社会", "Ryokuoushoku Shakai"),
            ("家入レオ", "家入レオ"),
            ("奥華子", "Hanako Oku"),
            ("テイラー・スウィフト", "Taylor Swift"),
            ("コーヒー", "Kohi"),
        };

    foreach (var (left, right) in pairs)
    {
        var verdict = TitleMatcher.IsSameTitle(left, right) ? "MATCH   " : "no match";
        var literal = TitleMatcher.IsSameLiteralTitle(left, right) ? "literal" : "-";
        Console.WriteLine(
            $"  {verdict}  {left,-16} vs {right,-22} literal={literal,-7} ({TitleMatcher.Normalize(left)} | {TitleMatcher.Normalize(right)})");
    }

    return 0;
}

var term = args.Length > 0 ? args[0] : "Taylor Swift 1989";

Console.WriteLine("Apple Music scrape smoke test");
Console.WriteLine($"Search term: {term}");
Console.WriteLine();

var logger = new ConsoleLogger();
var source = new WebMetadataSource(logger, new HttpClientStub());

Console.WriteLine("--- album search ---");
var albums = await source.SearchAsync(term, ItemType.Album, CancellationToken.None);
Console.WriteLine($"albums found: {albums.Count}");
foreach (var item in albums.Take(3))
{
    if (item is AppleMusicAlbum album)
    {
        Console.WriteLine($"  [{album.Id}] {album.Name}");
        Console.WriteLine($"      release : {album.ReleaseDate:yyyy-MM-dd}");
        Console.WriteLine($"      image   : {Trunc(album.ImageUrl)}");
        Console.WriteLine($"      about   : {Trunc(album.About)}");
        Console.WriteLine($"      artists : {string.Join(", ", album.Artists.Select(a => a.Name))}");
    }
}

Console.WriteLine();
Console.WriteLine("--- artist search ---");
var artists = await source.SearchAsync(term, ItemType.Artist, CancellationToken.None);
Console.WriteLine($"artists found: {artists.Count}");
foreach (var item in artists.Take(3))
{
    if (item is AppleMusicArtist artist)
    {
        Console.WriteLine($"  [{artist.Id}] {artist.Name}");
        Console.WriteLine($"      image : {Trunc(artist.ImageUrl)}");
        Console.WriteLine($"      about : {Trunc(artist.About)}");
    }
}

Console.WriteLine();
Console.WriteLine(albums.Count > 0 || artists.Count > 0 ? "RESULT: selectors work" : "RESULT: nothing found - selectors are likely stale");

return 0;

static string Trunc(string value)
{
    if (string.IsNullOrEmpty(value))
    {
        return "(none)";
    }

    var oneLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
    return oneLine.Length <= 110 ? oneLine : oneLine[..110] + "...";
}

internal sealed class ConsoleLogger : ILogger
{
    public void Info(string message, params object[] paramList) => Write("INFO", message, paramList);

    public void Error(string message, params object[] paramList) => Write("ERROR", message, paramList);

    public void Warn(string message, params object[] paramList) => Write("WARN", message, paramList);

    public void Debug(string message, params object[] paramList) => Write("DEBUG", message, paramList);

    public void Fatal(string message, params object[] paramList) => Write("FATAL", message, paramList);

    public void FatalException(string message, Exception exception, params object[] paramList)
    {
        Write("FATAL", message, paramList);
        Console.WriteLine(exception);
    }

    public void ErrorException(string message, Exception exception, params object[] paramList)
    {
        Write("ERROR", message, paramList);
        Console.WriteLine(exception);
    }

    public void LogMultiline(string message, LogSeverity severity, StringBuilder additionalContent)
    {
        Write(severity.ToString().ToUpperInvariant(), message, Array.Empty<object>());
        Console.WriteLine(additionalContent);
    }

    public void Log(LogSeverity severity, string message, params object[] paramList) => Write(severity.ToString().ToUpperInvariant(), message, paramList);

    public void Log(LogSeverity severity, ReadOnlyMemory<char> message) => Write(severity.ToString().ToUpperInvariant(), message.ToString(), Array.Empty<object>());

    public void Error(ReadOnlyMemory<char> message) => Write("ERROR", message.ToString(), Array.Empty<object>());

    public void Warn(ReadOnlyMemory<char> message) => Write("WARN", message.ToString(), Array.Empty<object>());

    public void Info(ReadOnlyMemory<char> message) => Write("INFO", message.ToString(), Array.Empty<object>());

    public void Debug(ReadOnlyMemory<char> message) => Write("DEBUG", message.ToString(), Array.Empty<object>());

    private static void Write(string level, string message, object[] paramList)
    {
        var text = paramList is { Length: > 0 } ? string.Format(message, paramList) : message;
        Console.WriteLine($"  [{level}] {text}");
    }
}
