using Microsoft.AspNetCore.SignalR;

namespace Aethernet.Server.Services;

/// <summary>
/// Process-local map of UID -> live hub connections on THIS instance. Presence (who's online,
/// anywhere in the fleet) is tracked by <see cref="IPresenceTracker"/>; this is the one piece
/// that can't live in Redis, because aborting a connection has to happen on whichever instance
/// actually holds the socket. <see cref="IBanEnforcementService"/> uses this for the local half
/// of a kick and relies on a Redis pub/sub fan-out (see <see cref="BanBroadcastListener"/>) to
/// reach the other half when the fleet has more than one instance.
/// </summary>
public interface IConnectionRegistry
{
    void Register(string uid, HubCallerContext context);
    void Unregister(string uid, string connectionId);

    /// <summary>Aborts every connection this instance holds for <paramref name="uid"/>. No-op if none.</summary>
    int AbortLocal(string uid);
}
