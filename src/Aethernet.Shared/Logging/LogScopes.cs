using Microsoft.Extensions.Logging;

namespace Aethernet.Shared.Logging;

/// <summary>
/// Convenience extension methods for opening structured log scopes consistently across services.
/// </summary>
public static class LogScopes
{
    public static IDisposable? UserScope(this ILogger logger, string uid) =>
        logger.BeginScope(new Dictionary<string, object> { ["uid"] = uid });

    public static IDisposable? GroupScope(this ILogger logger, string gid) =>
        logger.BeginScope(new Dictionary<string, object> { ["gid"] = gid });

    public static IDisposable? HubMethodScope(this ILogger logger, string method, string uid) =>
        logger.BeginScope(new Dictionary<string, object> { ["hub.method"] = method, ["uid"] = uid });
}
