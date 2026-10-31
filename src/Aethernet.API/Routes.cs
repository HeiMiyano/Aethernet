namespace Aethernet.API;

/// <summary>HTTP route constants for the auth service and file server.</summary>
public static class Routes
{
    public static class Auth
    {
        public const string Base                = "/auth";
        public const string Register            = "/auth/register";          // POST  — disabled (410): accounts are created via Discord
        public const string Login               = "/auth/login";             // POST  { uid, secret_key } -> { jwt, refresh, expires_in }
        public const string Refresh             = "/auth/refresh";           // POST  { refresh } -> { jwt, refresh, expires_in }
        public const string Logout              = "/auth/logout";            // POST  Authorization: Bearer ...
        public const string Recover             = "/auth/recover";           // POST  { recovery_secret } -> { uid, secret_key }
        public const string Me                  = "/auth/me";                // GET
        public const string DiscordBegin        = "/auth/discord/begin";     // POST  { mode, uid?, secretKey? } -> { authorizeUrl, pollToken, expiresInSeconds }
        public const string DiscordCallback     = "/auth/discord/callback";  // GET   Discord redirects the browser here (?code&state)
        public const string DiscordPoll         = "/auth/discord/poll";      // POST  { pollToken } -> { status, uid?, secretKey?, ... }
        public const string RotateSecret        = "/auth/secret/rotate";     // POST
    }

    public static class Files
    {
        public const string Base       = "/files";
        public const string Has        = "/files/has";         // POST { hashes: [...] } -> { missing: [...] }
        public const string Upload     = "/files/upload";      // POST multipart, hash in form
        public const string Download   = "/files/{hash}";      // GET, supports Range
        public const string Delete     = "/files/{hash}";      // DELETE (owner-only / admin)
        public const string Quota      = "/files/quota";       // GET
        public const string Stats      = "/files/stats";       // GET (admin)
    }

    public static class Server
    {
        public const string Hub        = AethernetConstants.HubPath; // SignalR mount point
        public const string Health     = "/healthz";
        public const string Ready      = "/readyz";
        public const string Metrics    = "/metrics";                  // prometheus
        public const string ApiInfo    = "/api/info";                 // returns { protocolVersion, build, motd }
    }
}
