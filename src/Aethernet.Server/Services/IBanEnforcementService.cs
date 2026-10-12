namespace Aethernet.Server.Services;

/// <summary>
/// Makes a ban take effect immediately for an already-connected user, instead of only on their
/// next reconnect (which is all <c>UserEntity.IsBanned</c> + the <c>OnConnectedAsync</c> check
/// alone gives you). Call this anywhere a ban is issued — the hub's own moderation methods and
/// the REST <c>/admin</c> surface both need it.
/// </summary>
public interface IBanEnforcementService
{
    /// <summary>
    /// Notifies <paramref name="uid"/> of the ban (if connected, anywhere in the fleet) and
    /// forcibly disconnects their live hub connection(s).
    /// </summary>
    Task EnforceBanAsync(string uid, string? reason);
}
