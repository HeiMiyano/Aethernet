namespace Aethernet.Plugin;

/// <summary>
/// Server endpoints are intentionally hard-coded — the distributed plugin will only ever
/// connect to the official Aethernet infrastructure. We don't expose these as user-editable
/// settings because doing so would invite forks running on unofficial servers, which would
/// fragment the user base and create support ambiguity. If you genuinely need to point at a
/// different deployment (e.g. local dev), edit this file and rebuild the plugin yourself.
/// </summary>
public static class AethernetServers
{
    public const string AuthUrl = "https://auth-aethernet.heimiyano.com/";
    public const string HubUrl  = "https://hub-aethernet.heimiyano.com/";
    public const string FileUrl = "https://files-aethernet.heimiyano.com/";
}
