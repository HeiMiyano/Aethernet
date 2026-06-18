using System.Collections.Concurrent;
using StackExchange.Redis;

namespace Aethernet.Server.Services;

/// <summary>
/// Presence tracker backed by Redis hashes. Falls back to a process-local concurrent dictionary
/// when Redis isn't configured (single-instance dev).
/// </summary>
public sealed class RedisPresenceTracker : IPresenceTracker
{
    private const string KeyPrefix  = "aethernet:presence:";   // uid -> hash of connectionId -> instance
    private const string IndexKey   = "aethernet:online";       // set of online uids
    private const string IdentPrefix = "aethernet:ident:";       // uid -> "Name@WorldID"

    private readonly IConnectionMultiplexer? _redis;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _local = new();
    private readonly ConcurrentDictionary<string, string> _localIdents = new();

    public RedisPresenceTracker(IConnectionMultiplexer? redis = null)
    {
        _redis = redis;
    }

    public async Task MarkOnlineAsync(string uid, string connectionId)
    {
        if (_redis is null)
        {
            var bucket = _local.GetOrAdd(uid, _ => new());
            bucket[connectionId] = 1;
            return;
        }
        var db = _redis.GetDatabase();
        await db.HashSetAsync(KeyPrefix + uid, connectionId, Environment.MachineName);
        await db.SetAddAsync(IndexKey, uid);
        await db.KeyExpireAsync(KeyPrefix + uid, TimeSpan.FromHours(24));
    }

    public async Task MarkOfflineAsync(string uid, string connectionId)
    {
        if (_redis is null)
        {
            if (_local.TryGetValue(uid, out var bucket))
            {
                bucket.TryRemove(connectionId, out _);
                if (bucket.IsEmpty) _local.TryRemove(uid, out _);
            }
            return;
        }
        var db = _redis.GetDatabase();
        await db.HashDeleteAsync(KeyPrefix + uid, connectionId);
        var remaining = await db.HashLengthAsync(KeyPrefix + uid);
        if (remaining == 0)
        {
            await db.SetRemoveAsync(IndexKey, uid);
            await db.KeyDeleteAsync(KeyPrefix + uid);
        }
    }

    public async Task<bool> IsOnlineAsync(string uid)
    {
        if (_redis is null) return _local.ContainsKey(uid);
        var db = _redis.GetDatabase();
        return await db.SetContainsAsync(IndexKey, uid);
    }

    public async Task<string?> GetPrimaryConnectionAsync(string uid)
    {
        if (_redis is null)
        {
            return _local.TryGetValue(uid, out var b) ? b.Keys.FirstOrDefault() : null;
        }
        var db = _redis.GetDatabase();
        var entries = await db.HashGetAllAsync(KeyPrefix + uid);
        return entries.Length == 0 ? null : entries[0].Name.ToString();
    }

    public async Task<int> OnlineCountAsync()
    {
        if (_redis is null) return _local.Count;
        var db = _redis.GetDatabase();
        return (int)await db.SetLengthAsync(IndexKey);
    }

    public async Task SetIdentAsync(string uid, string ident)
    {
        if (_redis is null) { _localIdents[uid] = ident; return; }
        var db = _redis.GetDatabase();
        await db.StringSetAsync(IdentPrefix + uid, ident, TimeSpan.FromHours(24));
    }

    public async Task<string?> GetIdentAsync(string uid)
    {
        if (_redis is null) return _localIdents.TryGetValue(uid, out var local) ? local : null;
        var db = _redis.GetDatabase();
        var val = await db.StringGetAsync(IdentPrefix + uid);
        return val.IsNullOrEmpty ? null : val.ToString();
    }
}
