using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugin.AppleMusic.MetadataSources.Json.ApiClient;

/// <summary>
/// Provides the bearer token required by the Apple Music JSON API.
/// </summary>
public interface IAppleMusicTokenProvider
{
    /// <summary>
    /// Gets a valid token, refreshing it when necessary.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Bearer token.</returns>
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
