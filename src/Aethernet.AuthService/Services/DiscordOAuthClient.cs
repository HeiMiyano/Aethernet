using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Aethernet.AuthService.Services;

public enum GuildMembership { Member, NotMember, RateLimited, Error }

public sealed record DiscordUser(string Id, string Username, string? GlobalName);

/// <summary>
/// Minimal hand-rolled Discord OAuth2 authorization-code client. We don't use the ASP.NET
/// Discord auth handler because it's built around a cookie sign-in in the same browser; here the
/// browser only proves who the Discord user is, and the result is handed to the plugin (a different
/// process) via <see cref="DiscordSignInStore"/>. The Discord access token is used for at most two
/// calls and then discarded — it is never stored.
/// </summary>
public sealed class DiscordOAuthClient
{
    private const string ApiBase      = "https://discord.com/api/v10";
    private const string AuthorizeUrl = "https://discord.com/oauth2/authorize";

    private readonly HttpClient _http;
    private readonly DiscordOAuthOptions _opt;
    private readonly ILogger<DiscordOAuthClient> _log;

    public DiscordOAuthClient(HttpClient http, IOptions<DiscordOAuthOptions> opt, ILogger<DiscordOAuthClient> log)
    {
        _http = http; _opt = opt.Value; _log = log;
    }

    private bool CheckGuild => !string.IsNullOrWhiteSpace(_opt.RequiredGuildId);

    public string BuildAuthorizeUrl(string state)
    {
        // guilds.members.read lets us ask "is this user in our server?" with one call, without
        // seeing the rest of their server list (the plain `guilds` scope would expose all of it).
        var scope = CheckGuild ? "identify guilds.members.read" : "identify";
        return QueryHelpers.AddQueryString(AuthorizeUrl, new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"]     = _opt.ClientId,
            ["scope"]         = scope,
            ["state"]         = state,
            ["redirect_uri"]  = _opt.RedirectUri,
            // Skip the consent screen when the user has already authorized these scopes before.
            ["prompt"]        = "none",
        });
    }

    public async Task<string?> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["redirect_uri"]  = _opt.RedirectUri!,
                ["client_id"]     = _opt.ClientId!,
                ["client_secret"] = _opt.ClientSecret!,
            }),
        };
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            // Body is Discord's OAuth error JSON (e.g. invalid_grant) — no secrets in it.
            _log.LogWarning("Discord token exchange failed: {Status} {Body}",
                (int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
            return null;
        }
        var tok = await resp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        return string.IsNullOrEmpty(tok?.AccessToken) ? null : tok.AccessToken;
    }

    public async Task<DiscordUser?> GetCurrentUserAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/users/@me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogWarning("Discord /users/@me failed: {Status}", (int)resp.StatusCode);
            return null;
        }
        var me = await resp.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: ct);
        return string.IsNullOrEmpty(me?.Id) ? null : new DiscordUser(me.Id, me.Username ?? "", me.GlobalName);
    }

    public async Task<GuildMembership> CheckGuildMembershipAsync(string accessToken, CancellationToken ct)
    {
        if (!CheckGuild) return GuildMembership.Member;

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"{ApiBase}/users/@me/guilds/{_opt.RequiredGuildId}/member");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.IsSuccessStatusCode) return GuildMembership.Member;
        switch (resp.StatusCode)
        {
            case HttpStatusCode.NotFound:          // "Unknown Guild" — user isn't a member
                return GuildMembership.NotMember;
            case HttpStatusCode.TooManyRequests:
                return GuildMembership.RateLimited;
            default:
                _log.LogWarning("Discord guild-member check failed: {Status}", (int)resp.StatusCode);
                return GuildMembership.Error;
        }
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record MeResponse(
        [property: JsonPropertyName("id")]          string? Id,
        [property: JsonPropertyName("username")]    string? Username,
        [property: JsonPropertyName("global_name")] string? GlobalName);
}
