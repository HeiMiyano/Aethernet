using System.Security.Claims;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.AuthService.Services;
using Aethernet.Shared.Observability;
using Aethernet.Data;
using Aethernet.Shared.Identity;
using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.AuthService.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AethernetDbContext _db;
    private readonly IRegistrationService _reg;
    private readonly IRefreshTokenService _refresh;
    private readonly IJwtIssuer _jwt;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        AethernetDbContext db,
        IRegistrationService reg,
        IRefreshTokenService refresh,
        IJwtIssuer jwt,
        ILogger<AuthController> log)
    {
        _db = db; _reg = reg; _refresh = refresh; _jwt = jwt; _log = log;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RegisterResponseDto>> Register([FromBody] RegisterRequestDto body)
    {
        // Anonymous registration is allowed in dev; for prod you'd require Discord OAuth first.
        var discordId = User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _reg.RegisterAsync(body, discordId);
        AethernetMetrics.AuthRegistrations.WithLabels(string.IsNullOrEmpty(discordId) ? "anonymous" : "discord").Inc();
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto body)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Uid == body.UID);
        if (user is null || user.IsBanned) { AethernetMetrics.AuthLoginAttempts.WithLabels("banned_or_missing").Inc(); return Unauthorized(); }
        if (!SecretKeyHasher.Verify(body.SecretKey, user.SecretKeyHash)) { AethernetMetrics.AuthLoginAttempts.WithLabels("bad_secret").Inc(); return Unauthorized(); }
        AethernetMetrics.AuthLoginAttempts.WithLabels("success").Inc();

        var (token, expires) = _jwt.IssueAccessToken(user.Uid, user.IsAdmin, user.IsModerator);
        var (_, refresh)     = await _refresh.IssueAsync(user.Uid,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString());
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new LoginResponseDto(token, refresh, (int)(expires - DateTime.UtcNow).TotalSeconds));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponseDto>> Refresh([FromBody] RefreshRequestDto body)
    {
        var newRefresh = await _refresh.ValidateAndRotateAsync(body.RefreshToken,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString());
        if (newRefresh is null) { AethernetMetrics.AuthRefreshes.WithLabels("invalid").Inc(); return Unauthorized(); }
        AethernetMetrics.AuthRefreshes.WithLabels("success").Inc();

        // Look up the user from the new (now-valid) refresh token chain.
        var tokenId = newRefresh.Split('.', 2)[0];
        var row = await _db.RefreshTokens.AsNoTracking().FirstAsync(t => t.TokenId == tokenId);
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Uid == row.Uid);

        var (token, expires) = _jwt.IssueAccessToken(user.Uid, user.IsAdmin, user.IsModerator);
        return Ok(new LoginResponseDto(token, newRefresh, (int)(expires - DateTime.UtcNow).TotalSeconds));
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        await _refresh.RevokeAllForUserAsync(uid);
        return NoContent();
    }

    [HttpPost("recover")]
    public async Task<ActionResult<RegisterResponseDto>> Recover([FromBody] Dictionary<string, string> body)
    {
        if (!body.TryGetValue("recovery_secret", out var secret) || string.IsNullOrWhiteSpace(secret))
            return BadRequest();
        try
        {
            return Ok(await _reg.RecoverAsync(secret));
        }
        catch (InvalidOperationException)
        {
            return Unauthorized();
        }
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpGet("me")]
    public async Task<ActionResult<MeResponseDto>> Me()
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Uid == uid);
        if (u is null) return NotFound();
        return Ok(new MeResponseDto(u.Uid, u.Alias, u.IsAdmin, u.IsModerator, u.CreatedAt));
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [HttpPost("secret/rotate")]
    public async Task<ActionResult<RegisterResponseDto>> RotateSecret()
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        return Ok(await _reg.RotateSecretAsync(uid));
    }

    // ---- Discord OAuth ----------------------------------------------------

    [HttpGet("oauth/discord")]
    public IActionResult DiscordStart([FromQuery] string? returnUrl = null) =>
        Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/auth/oauth/discord/cb" },
                  DiscordAuthenticationDefaults.AuthenticationScheme);

    [HttpGet("oauth/discord/cb")]
    public async Task<IActionResult> DiscordCallback()
    {
        var auth = await HttpContext.AuthenticateAsync(DiscordAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded) return Unauthorized();
        var discordId = auth.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var result = await _reg.RegisterAsync(new RegisterRequestDto(null), discordId);
        Response.ContentType = "text/html; charset=utf-8";
        return Content(SuccessPageHtml(result.UID, result.SecretKey));
    }

    private static string SuccessPageHtml(string uid, string secretKey) => @"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8"">
<title>Aethernet — account created</title>
<style>
  :root { color-scheme: dark light; }
  body { font-family: system-ui, -apple-system, sans-serif; max-width: 640px; margin: 4rem auto;
         padding: 0 1rem; line-height: 1.5; }
  h1 { margin-bottom: 0.25rem; }
  .sub { color: #888; margin-bottom: 2rem; }
  .card { background: #f4f4f7; border-radius: 8px; padding: 1rem 1.25rem; margin: 1rem 0; }
  @media (prefers-color-scheme: dark) { .card { background: #1c1c1f; } }
  .label { font-size: .78rem; text-transform: uppercase; letter-spacing: .05em; color: #888; }
  .value { font-family: ui-monospace, SF Mono, Menlo, monospace; font-size: 1.1rem;
           overflow-wrap: anywhere; padding: .25rem 0; }
  button { background: #4a90e2; color: white; border: none; padding: .4rem .9rem;
           border-radius: 6px; cursor: pointer; font-size: .9rem; }
  button:hover { background: #357ab8; }
  .warn { border-left: 3px solid #d97706; padding-left: .75rem; color: #92400e; }
  @media (prefers-color-scheme: dark) { .warn { color: #fbbf24; } }
</style>
</head><body>
  <h1>Account created</h1>
  <p class=""sub"">Save both of these values now. The secret key will <strong>never be shown again</strong>.</p>

  <div class=""card"">
    <div class=""label"">User ID (UID)</div>
    <div class=""value"" id=""uid"">__UID__</div>
    <button onclick=""copy('uid')"">Copy UID</button>
  </div>

  <div class=""card"">
    <div class=""label"">Secret key</div>
    <div class=""value"" id=""key"">__KEY__</div>
    <button onclick=""copy('key')"">Copy secret key</button>
  </div>

  <p class=""warn"">Anyone with your secret key can sign in as you. Treat it like a password.
     Paste both values into Aethernet → Settings → Account, then close this tab.</p>

  <script>
    function copy(id) {
      const t = document.getElementById(id).innerText;
      navigator.clipboard.writeText(t).then(() => {
        const b = event.target;
        const orig = b.innerText;
        b.innerText = 'Copied';
        setTimeout(() => b.innerText = orig, 1200);
      });
    }
  </script>
</body></html>".Replace("__UID__", System.Net.WebUtility.HtmlEncode(uid))
              .Replace("__KEY__", System.Net.WebUtility.HtmlEncode(secretKey));
}
