using System.Net;
using Aethernet.API.Dto;
using Aethernet.AuthService.Services;
using Aethernet.Data;
using Aethernet.Data.Entities;
using Aethernet.Shared.Identity;
using Aethernet.Shared.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aethernet.AuthService.Controllers;

/// <summary>
/// The only way to create an Aethernet account: "Sign in with Discord" from the plugin.
///
///   1. plugin  POST /auth/discord/begin    → { authorizeUrl, pollToken }
///   2. plugin  opens authorizeUrl in the user's browser; user clicks Authorize on Discord
///   3. Discord redirects the browser to GET /auth/discord/callback?code&amp;state
///      → we identify the Discord user, then create / sign in to / link the account
///   4. plugin  POST /auth/discord/poll     → { status: complete, uid, secretKey }
///
/// Credentials never appear in the browser — the callback page only says "go back to the game".
/// </summary>
[ApiController]
[Route("auth/discord")]
public sealed class DiscordAuthController : ControllerBase
{
    private readonly AethernetDbContext _db;
    private readonly DiscordSignInStore _store;
    private readonly DiscordOAuthClient _discord;
    private readonly IRefreshTokenService _refresh;
    private readonly DiscordOAuthOptions _opt;
    private readonly ILogger<DiscordAuthController> _log;

    public DiscordAuthController(
        AethernetDbContext db, DiscordSignInStore store, DiscordOAuthClient discord,
        IRefreshTokenService refresh, IOptions<DiscordOAuthOptions> opt, ILogger<DiscordAuthController> log)
    {
        _db = db; _store = store; _discord = discord; _refresh = refresh; _opt = opt.Value; _log = log;
    }

    [HttpPost("begin")]
    public async Task<IActionResult> Begin([FromBody] DiscordBeginRequestDto body)
    {
        if (!_opt.IsConfigured)
            return StatusCode(503, new AuthErrorDto(AuthErrorCodes.DiscordNotConfigured,
                "Discord sign-in isn't set up on this server yet."));

        var mode = body.Mode?.Trim().ToLowerInvariant();
        string? linkUid = null;

        if (mode == DiscordAuthModes.Link)
        {
            if (string.IsNullOrWhiteSpace(body.UID) || string.IsNullOrEmpty(body.SecretKey))
                return BadRequest(new AuthErrorDto(AuthErrorCodes.BadRequest, "Linking needs your UID and secret key."));

            var uid  = body.UID.Trim();
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Uid == uid);
            if (user is null || !SecretKeyHasher.Verify(body.SecretKey, user.SecretKeyHash))
                return Unauthorized(new AuthErrorDto(AuthErrorCodes.InvalidCredentials,
                    "That UID and secret key don't match an account."));
            if (user.IsBanned)
                return StatusCode(403, new AuthErrorDto(AuthErrorCodes.Banned, "This account is banned."));
            if (!string.IsNullOrEmpty(user.DiscordUserId))
                return Conflict(new AuthErrorDto(AuthErrorCodes.AlreadyLinked,
                    "This account is already linked to a Discord account."));
            linkUid = user.Uid;
        }
        else if (mode != DiscordAuthModes.SignIn)
        {
            return BadRequest(new AuthErrorDto(AuthErrorCodes.BadRequest, $"Unknown mode '{body.Mode}'."));
        }

        var session = _store.Create(mode, linkUid);
        if (session is null)
            return StatusCode(503, new AuthErrorDto(AuthErrorCodes.Busy,
                "Too many sign-ins in progress right now — try again in a minute."));

        return Ok(new DiscordBeginResponseDto(
            _discord.BuildAuthorizeUrl(session.State), session.PollToken, (int)DiscordSignInStore.Lifetime.TotalSeconds));
    }

    [HttpPost("poll")]
    public ActionResult<DiscordPollResponseDto> Poll([FromBody] DiscordPollRequestDto body) =>
        Ok(_store.Poll(body.PollToken));

    [HttpGet("callback")]
    public async Task<ContentResult> Callback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        var session = _store.Claim(state);
        if (session is null)
            return Page(false, "This sign-in link has expired",
                "Go back to the game and click <b>Sign in with Discord</b> again.");

        DiscordPollResponseDto result;
        try
        {
            result = await ProcessAsync(session, code, error, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Discord callback failed");
            result = Fail("server_error", "Something went wrong on the Aethernet server. Please try again.");
        }

        _store.Complete(session, result);
        _log.LogInformation("Discord {Mode} finished: {Status} {Error} uid={Uid}",
            session.Mode, result.Status, result.Error, result.UID);

        return result.Status == DiscordPollStatus.Complete
            ? Page(true, "You're all set",
                "You can close this tab and go back to the game — Aethernet will connect on its own.")
            : Page(false, "Sign-in didn't finish", Linkify(WebUtility.HtmlEncode(result.Message ?? "")));
    }

    private async Task<DiscordPollResponseDto> ProcessAsync(
        DiscordSignInSession session, string? code, string? error, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            return Fail("cancelled", "Discord authorization was cancelled.");

        var accessToken = await _discord.ExchangeCodeAsync(code, ct);
        if (accessToken is null) return Fail("discord_error", "Discord didn't accept the sign-in. Please try again.");

        var du = await _discord.GetCurrentUserAsync(accessToken, ct);
        if (du is null) return Fail("discord_error", "Couldn't read your Discord account. Please try again.");

        switch (await _discord.CheckGuildMembershipAsync(accessToken, ct))
        {
            case GuildMembership.Member: break;
            case GuildMembership.NotMember:
                return Fail("not_in_server", string.IsNullOrWhiteSpace(_opt.InviteUrl)
                    ? "You need to join the Aethernet Discord server first, then try again."
                    : $"You need to join the Aethernet Discord server first ({_opt.InviteUrl}), then try again.");
            case GuildMembership.RateLimited:
                return Fail("discord_rate_limited", "Discord is rate-limiting us — wait a minute and try again.");
            default:
                return Fail("discord_error", "Couldn't check your Discord server membership. Please try again.");
        }

        return session.Mode == DiscordAuthModes.Link
            ? await LinkAsync(session.LinkUid!, du, ct)
            : await SignInAsync(du, ct);
    }

    private async Task<DiscordPollResponseDto> SignInAsync(DiscordUser du, CancellationToken ct)
    {
        var existing = await _db.Users.FirstOrDefaultAsync(u => u.DiscordUserId == du.Id, ct);
        if (existing is not null)
        {
            if (existing.IsBanned)
                return Fail(AuthErrorCodes.Banned, string.IsNullOrWhiteSpace(existing.BanReason)
                    ? "This account is banned." : $"This account is banned: {existing.BanReason}");

            // Signing in again = new PC or lost key. Issue a fresh secret and kill old sessions;
            // the previous key stops working everywhere else.
            var rotated = UidGenerator.NewSecretKey();
            existing.SecretKeyHash = SecretKeyHasher.Hash(rotated);
            await _db.SaveChangesAsync(ct);
            await _refresh.RevokeAllForUserAsync(existing.Uid);
            AethernetMetrics.AuthLoginAttempts.WithLabels("discord_signin").Inc();
            return new DiscordPollResponseDto(DiscordPollStatus.Complete, existing.Uid, rotated, true, null,
                "Signed in to your existing account. If you use Aethernet on another PC, sign in with Discord there too — the old secret key no longer works.");
        }

        var uid = UidGenerator.NewUid();
        var key = UidGenerator.NewSecretKey();
        _db.Users.Add(new UserEntity
        {
            Uid           = uid,
            SecretKeyHash = SecretKeyHasher.Hash(key),
            DiscordUserId = du.Id,
            CreatedAt     = DateTime.UtcNow,
            LastSeenAt    = DateTime.UtcNow,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Unique index on Users.DiscordUserId: a parallel sign-in for the same Discord user won.
            return Fail("try_again", "Another sign-in for this Discord account just finished. Please try again.");
        }
        AethernetMetrics.AuthRegistrations.WithLabels("discord").Inc();
        return new DiscordPollResponseDto(DiscordPollStatus.Complete, uid, key, false, null, "Account created.");
    }

    private async Task<DiscordPollResponseDto> LinkAsync(string uid, DiscordUser du, CancellationToken ct)
    {
        var other = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.DiscordUserId == du.Id && u.Uid != uid, ct);
        if (other is not null)
            return Fail("discord_already_linked",
                $"That Discord account is already linked to a different Aethernet account ({other.Uid}). " +
                "Link a different Discord account, or use Sign in with Discord to switch to that one.");

        var target = await _db.Users.FirstOrDefaultAsync(u => u.Uid == uid, ct);
        if (target is null) return Fail("not_found", "That account no longer exists.");
        if (target.DiscordUserId == du.Id)
            return new DiscordPollResponseDto(DiscordPollStatus.Complete, uid, null, true, null, "Already linked.");
        if (!string.IsNullOrEmpty(target.DiscordUserId))
            return Fail(AuthErrorCodes.AlreadyLinked, "This account is already linked to a different Discord account.");

        target.DiscordUserId = du.Id;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Fail("discord_already_linked", "That Discord account was just linked to another Aethernet account.");
        }
        return new DiscordPollResponseDto(DiscordPollStatus.Complete, uid, null, true, null, "Discord linked.");
    }

    private static DiscordPollResponseDto Fail(string code, string message) =>
        new(DiscordPollStatus.Failed, null, null, false, code, message);

    /// <summary>Turns an (already HTML-encoded) invite URL in the message into a clickable link.</summary>
    private string Linkify(string encoded)
    {
        if (string.IsNullOrWhiteSpace(_opt.InviteUrl)) return encoded;
        var url = WebUtility.HtmlEncode(_opt.InviteUrl);
        return encoded.Replace(url, $"<a href=\"{url}\">{url}</a>");
    }

    private static ContentResult Page(bool ok, string title, string bodyHtml) => new()
    {
        StatusCode  = 200,
        ContentType = "text/html; charset=utf-8",
        Content = $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Aethernet</title>
            <style>
              :root { color-scheme: light dark; --fg:#1d1d22; --muted:#6b6b76; --card:#f4f4f7; --accent:{{(ok ? "#2f9e5b" : "#c2410c")}}; }
              @media (prefers-color-scheme: dark) { :root { --fg:#ececf1; --muted:#9a9aa6; --card:#1c1c21; --accent:{{(ok ? "#4ade80" : "#fb923c")}}; } }
              body { margin:0; font-family:system-ui,-apple-system,"Segoe UI",sans-serif; color:var(--fg); background:Canvas; }
              main { max-width:520px; margin:12vh auto 0; padding:0 16px; }
              .card { background:var(--card); border-radius:12px; padding:28px 28px 24px; border-top:4px solid var(--accent); }
              h1 { font-size:1.35rem; margin:0 0 .6rem; }
              p { margin:0; line-height:1.55; color:var(--muted); }
              a { color:var(--accent); }
            </style></head>
            <body><main><div class="card"><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{bodyHtml}}</p></div></main></body></html>
            """,
    };
}
