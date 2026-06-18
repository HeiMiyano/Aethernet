namespace Aethernet.API;

/// <summary>
/// Protocol-level constants shared between client and server.
/// Changes here are breaking — bump <see cref="ProtocolVersion"/>.
/// </summary>
public static class AethernetConstants
{
    /// <summary>
    /// Incremented whenever the hub contract changes in a backwards-incompatible way.
    /// Clients send this in the connection handshake and the hub rejects mismatched versions.
    /// </summary>
    public const int ProtocolVersion = 1;

    /// <summary>SignalR hub path on the main server.</summary>
    public const string HubPath = "/aethernet";

    /// <summary>Header used to pass the protocol version during the SignalR negotiate step.</summary>
    public const string ProtocolHeader = "X-Aethernet-Protocol";

    /// <summary>Header carrying the client build version (informational, used for ops).</summary>
    public const string ClientVersionHeader = "X-Aethernet-Client";

    /// <summary>Maximum size, in bytes, of a single file blob the file server accepts.</summary>
    public const long MaxFileSize = 1L * 1024 * 1024 * 1024; // 1 GiB

    /// <summary>Maximum size, in bytes, of a character-data envelope on the hub.</summary>
    public const int MaxCharacterDataBytes = 8 * 1024 * 1024; // 8 MiB

    /// <summary>Maximum number of pairs an account may have.</summary>
    public const int MaxPairs = 250;

    /// <summary>Maximum number of groups (syncshells) an account may belong to.</summary>
    public const int MaxGroupsJoined = 12;

    /// <summary>Maximum number of groups (syncshells) an account may own.</summary>
    public const int MaxGroupsOwned = 4;

    /// <summary>Maximum number of users a single group may contain.</summary>
    public const int MaxGroupUsers = 100;
}
