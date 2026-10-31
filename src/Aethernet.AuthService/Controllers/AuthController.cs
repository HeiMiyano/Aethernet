using System.Security.Claims;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.AuthService.Services;
using Aethernet.Shared.Observability;
using Aethernet.Data;
using Aethernet.Shared.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aethernet.AuthService.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AethernetDbContext _db;
    private readonly IRegistrationService _reg;
    private readonly IRefreshTokenService _refresh;
    private readonly IJwtIssuer _jwt;
    private readonly DiscordOAuthOptions _discord;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        AethernetDbContext db,
        IRegistrationService reg,
        IRefreshTokenService refresh,
        IJwtIssuer jwt,
        IOptions<DiscordOAuthOptions> discord,
        ILogger<AuthController> log)
    {
        _db = db; _reg = reg; _refresh = refresh; _jwt = jwt; _discord = discord.Value; _log = log;
    }

    private ObjectResult DiscordLinkRequired() => StatusCode(403, new AuthErrorDto(
        AuthErrorCodes.DiscordLinkRequired,
        "This account has to be linked to Discord before it can connect. Use Link Discord in the Aethernet plugin."));

    /// <summary>Anonymous registration is gone — accounts are created only through
    /// <see cref="DiscordAuthController"/>. Kept as an explicit 410 so old plugin builds get a
    /// readable reason instead of a bare 404.</summary>
    [HttpPost("register")]
    public IActionResult Register() =>
        StatusCode(StatusCodes.Status410Gone, new AuthErrorDto(AuthErrorCodes.RegistrationDisabled,
            "New accounts are created with Discord. Update the Aethernet plugin and use Sign in with Discord."));

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto body)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Uid == body.UID);
        if (user is null || user.IsBanned) { AethernetMetrics.AuthLoginAttempts.WithLabels("banned_or_missing").Inc(); return Unauthorized(); }
        if (!SecretKeyHasher.Verify(body.SecretKey, user.SecretKeyHash)) { AethernetMetrics.AuthLoginAttempts.WithLabels("bad_secret").Inc(); return Unauthorized(); }
        // Checked only after the secret verifies, so link status isn't revealed to non-owners.
        if (_discord.RequireLinkedAccount && string.IsNullOrEmpty(user.DiscordUserId))
        {
            AethernetMetrics.AuthLoginAttempts.WithLabels("discord_link_required").Inc();
            return DiscordLinkRequired();
        }
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
        if (user.IsBanned) return Unauthorized();
        if (_discord.RequireLinkedAccount && string.IsNullOrEmpty(user.DiscordUserId)) return DiscordLinkRequired();

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
}
