using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace Aethernet.Server.Services;

public sealed class ConnectionRegistry : IConnectionRegistry
{
    // uid -> (connectionId -> context)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HubCallerContext>> _byUid = new();

    public void Register(string uid, HubCallerContext context)
    {
        var bucket = _byUid.GetOrAdd(uid, _ => new());
        bucket[context.ConnectionId] = context;
    }

    public void Unregister(string uid, string connectionId)
    {
        if (!_byUid.TryGetValue(uid, out var bucket)) return;
        bucket.TryRemove(connectionId, out _);
        if (bucket.IsEmpty) _byUid.TryRemove(uid, out _);
    }

    public int AbortLocal(string uid)
    {
        if (!_byUid.TryRemove(uid, out var bucket)) return 0;
        var n = 0;
        foreach (var ctx in bucket.Values)
        {
            try { ctx.Abort(); n++; }
            catch { /* connection may already be tearing down; nothing to do */ }
        }
        return n;
    }
}
