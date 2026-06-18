using System.Collections.Concurrent;

namespace Aethernet.Server.Services;

public interface IRateLimiter
{
    bool TryConsume(string key, int maxPerMinute);
}

/// <summary>Token-bucket-ish per-key limiter. Good enough for "you can't push character data 200x/sec".</summary>
public sealed class InMemoryRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();

    public bool TryConsume(string key, int maxPerMinute)
    {
        var now = DateTime.UtcNow;
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket { WindowStart = now, Count = 0 });
        lock (bucket)
        {
            if (now - bucket.WindowStart > TimeSpan.FromMinutes(1))
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }
            if (bucket.Count >= maxPerMinute) return false;
            bucket.Count++;
            return true;
        }
    }

    private sealed class Bucket
    {
        public DateTime WindowStart;
        public int Count;
    }
}
