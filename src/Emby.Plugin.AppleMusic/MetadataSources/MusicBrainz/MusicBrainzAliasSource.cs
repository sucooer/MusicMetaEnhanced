using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.AppleMusic.MetadataSources.MusicBrainz;

/// <summary>
/// Reads the aliases of an artist from MusicBrainz. Apple Music localizes some artist
/// names (奥華子 is shown as "Hanako Oku"), which can never match the library name.
/// The MusicBrainz entry of the very same artist carries those localized spellings as
/// aliases, so they are used as a bridge between the two names.
/// </summary>
public static class MusicBrainzAliasSource
{
    private const string BaseUrl = "https://musicbrainz.org/ws/2/artist";

    /// <summary>
    /// MusicBrainz rate limits unauthenticated requests; an alias lookup only happens when
    /// the exact name match failed, and the cache keeps repeated refreshes from re-querying.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string[]> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets all alias spellings MusicBrainz knows for an artist.
    /// </summary>
    /// <param name="musicBrainzId">MusicBrainz artist ID, if known.</param>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Alias names, possibly empty.</returns>
    public static async Task<IReadOnlyList<string>> GetAliasesAsync(string? musicBrainzId, ISimpleHttpClient httpClient, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(musicBrainzId))
        {
            return Array.Empty<string>();
        }

        if (Cache.TryGetValue(musicBrainzId!, out var cached))
        {
            return cached;
        }

        var aliases = Array.Empty<string>();
        try
        {
            var url = $"{BaseUrl}/{Uri.EscapeDataString(musicBrainzId!)}?inc=aliases&fmt=json";
            var json = await httpClient.GetStringAsync(url, new Dictionary<string, string>(), cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("aliases", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var alias in list.EnumerateArray())
                {
                    if (alias.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    {
                        var value = name.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            names.Add(value!);
                        }
                    }
                }

                aliases = names.ToArray();
            }
        }
        catch (Exception exception)
        {
            // Aliases are only the third matching attempt - a failure here must never
            // break an otherwise working metadata refresh.
            logger.Debug("MusicBrainz: alias lookup for {0} failed: {1}", musicBrainzId, exception.Message);
        }

        Cache[musicBrainzId!] = aliases;
        return aliases;
    }
}
