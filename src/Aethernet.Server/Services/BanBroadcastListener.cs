using StackExchange.Redis;

namespace Aethernet.Server.Services;

/// <summary>
/// The other half of <see cref="BanEnforcementService"/>'s cross-instance kick: subscribes to
/// the fan-out channel and aborts any connection THIS instance happens to be holding for the
/// banned UID. Only registered when Redis is configured — with a single instance there's nothing
/// to fan out to, <see cref="BanEnforcementService"/> already handled the local abort directly.
/// </summary>
public sealed class BanBroadcastListener : BackgroundService
{
    private const string Channel = "aethernet:force-disconnect";

    private readonly IConnectionMultiplexer _redis;
    private readonly IConnectionRegistry _connections;
    private readonly ILogger<BanBroadcastListener> _log;

    public BanBroadcastListener(IConnectionMultiplexer redis, IConnectionRegistry connections, ILogger<BanBroadcastListener> log)
    {
        _redis = redis; _connections = connections; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sub = _redis.GetSubscriber();
        await sub.SubscribeAsync(RedisChannel.Literal(Channel), (_, uid) =>
        {
            if (uid.IsNullOrEmpty) return;
            var n = _connections.AbortLocal(uid.ToString());
            if (n > 0) _log.LogInformation("force-disconnect broadcast: aborted {Count} local connection(s) for {Uid}", n, uid);
        });

        // SubscribeAsync's callback runs for the life of the subscription; just wait for shutdown.
        await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }
}
