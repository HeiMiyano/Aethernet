namespace Aethernet.Server.Services;

/// <summary>
/// Tracks which UIDs have at least one live SignalR connection on any hub instance.
/// In production we back this with Redis so it works across pods.
/// </summary>
public interface IPresenceTracker
{
    Task MarkOnlineAsync(string uid, string connectionId);
    Task MarkOfflineAsync(string uid, string connectionId);
    Task<bool> IsOnlineAsync(string uid);
    Task<string?> GetPrimaryConnectionAsync(string uid);
    Task<int> OnlineCountAsync();

    // Character ident — "{Name}@{HomeWorldRowId}" published by the client. Used by paired
    // clients' VisibleUserManager to match against the live in-game object table.
    Task SetIdentAsync(string uid, string ident);
    Task<string?> GetIdentAsync(string uid);
}
