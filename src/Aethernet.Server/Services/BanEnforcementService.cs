using Aethernet.API.Dto;
using Aethernet.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace Aethernet.Server.Services;

/// <summary>
/// See <see cref="IBanEnforcementService"/>. The three steps below cover both deployment shapes
/// this codebase supports: a single instance (dev / self-hosted VM) and a Redis-backed fleet.
/// </summary>
public sealed class BanEnforcementService : IBanEnforcementService
{
    private const string Channel = "aethernet:force-disconnect";

    private readonly IHubContext<AethernetHub, IAethernetHubClient> _hub;
    private readonly IConnectionRegistry _connections;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<BanEnforcementService> _log;

    public BanEnforcementService(
        IHubContext<AethernetHub, IAethernetHubClient> hub,
        IConnectionRegistry connections,
        ILogger<BanEnforcementService> log,
        IConnectionMultiplexer? redis = null)
    {
        _hub = hub; _connections = connections; _log = log; _redis = redis;
    }

    public async Task EnforceBanAsync(string uid, string? reason)
    {
        // 1. Tell them why, wherever they're connected — SignalR's user-id routing fans this
        //    out across the Redis backplane automatically (see ClaimsNameIdentifierUserIdProvider).
        await _hub.Clients.User(uid).Client_ReceiveServerMessage(
            new ServerMessageDto(MessageSeverity.Error, $"You have been banned: {reason ?? "(no reason given)"}"));

        // 2. Abort the connection if it's local to this instance.
        var abortedLocally = _connections.AbortLocal(uid);

        // 3. Tell every other instance to do the same, in case the connection lives there instead.
        if (_redis is not null)
        {
            try { await _redis.GetSubscriber().PublishAsync(RedisChannel.Literal(Channel), uid); }
            catch (Exception ex) { _log.LogWarning(ex, "failed to publish force-disconnect for {Uid}", uid); }
        }

        _log.LogInformation("ban enforced for {Uid}: {Count} local connection(s) aborted, redis={HasRedis}",
            uid, abortedLocally, _redis is not null);
    }
}
