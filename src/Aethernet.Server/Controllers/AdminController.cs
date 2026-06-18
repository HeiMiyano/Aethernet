using System.Security.Claims;
using Aethernet.Data;
using Aethernet.Data.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.Server.Controllers;

/// <summary>
/// Web-friendly REST surface for the moderation dashboard. The hub already has
/// equivalent SignalR methods, but a moderation UI built in HTML is much easier
/// when it can just `fetch()` the JSON.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "admin,moderator")]
[Route("admin")]
public sealed class AdminController : ControllerBase
{
    private readonly AethernetDbContext _db;
    public AdminController(AethernetDbContext db) { _db = db; }

    private string Uid => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public record BanRequest(string? Reason);

    [HttpGet("reports")]
    public async Task<IActionResult> Reports(
        [FromQuery] bool resolved = false, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var rows = await _db.ProfileReports
            .Where(r => r.Resolved == resolved)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
        return Ok(rows.Select(r => new
        {
            id          = r.Id,
            reporter    = r.ReporterUid,
            reported    = r.ReportedUid,
            reason      = r.Reason,
            createdAt   = r.CreatedAt,
            resolved    = r.Resolved,
            resolution  = r.ResolutionNote,
        }));
    }

    [HttpPost("reports/{id:long}/resolve")]
    public async Task<IActionResult> ResolveReport(long id, [FromBody] Dictionary<string, string?> body, CancellationToken ct)
    {
        var row = await _db.ProfileReports.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return NotFound();
        row.Resolved        = true;
        row.ResolvedAt      = DateTime.UtcNow;
        body.TryGetValue("note", out var note);
        row.ResolutionNote  = note;
        _db.AuditLog.Add(new AuditLogEntity
        {
            ActorUid = Uid, Action = "report.resolve", TargetUid = row.ReportedUid,
            Detail = note, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Renamed from `User` to avoid shadowing ControllerBase.User (the HttpContext.User principal),
    // which broke the `Uid => User.FindFirstValue(...)` accessor above with CS0119.
    [HttpGet("users/{uid}")]
    public async Task<IActionResult> GetUser(string uid, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Uid == uid, ct);
        if (u is null) return NotFound();
        var pairs        = await _db.Pairs.CountAsync(p => p.OwnerUid == uid, ct);
        var groups       = await _db.GroupPairs.CountAsync(g => g.Uid == uid, ct);
        var reportsOpen  = await _db.ProfileReports.CountAsync(r => r.ReportedUid == uid && !r.Resolved, ct);
        return Ok(new
        {
            uid          = u.Uid,
            alias        = u.Alias,
            isAdmin      = u.IsAdmin,
            isModerator  = u.IsModerator,
            isBanned     = u.IsBanned,
            banReason    = u.BanReason,
            createdAt    = u.CreatedAt,
            lastSeenAt   = u.LastSeenAt,
            pairs, groups, openReports = reportsOpen,
        });
    }

    [HttpPost("users/{uid}/ban")]
    public async Task<IActionResult> Ban(string uid, [FromBody] BanRequest req, CancellationToken ct)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Uid == uid, ct);
        if (u is null) return NotFound();
        u.IsBanned = true; u.BanReason = req.Reason;
        if (!await _db.BannedUsers.AnyAsync(b => b.Uid == uid, ct))
            _db.BannedUsers.Add(new BannedUserEntity { Uid = uid, Reason = req.Reason, BannedAt = DateTime.UtcNow });
        _db.AuditLog.Add(new AuditLogEntity
        {
            ActorUid = Uid, Action = "user.ban", TargetUid = uid, Detail = req.Reason, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("users/{uid}/unban")]
    public async Task<IActionResult> Unban(string uid, CancellationToken ct)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Uid == uid, ct);
        if (u is null) return NotFound();
        u.IsBanned = false; u.BanReason = null;
        var ban = await _db.BannedUsers.FirstOrDefaultAsync(b => b.Uid == uid, ct);
        if (ban is not null) _db.BannedUsers.Remove(ban);
        _db.AuditLog.Add(new AuditLogEntity
        {
            ActorUid = Uid, Action = "user.unban", TargetUid = uid, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var rows = await _db.AuditLog.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var users        = await _db.Users.CountAsync(ct);
        var bannedUsers  = await _db.Users.CountAsync(u => u.IsBanned, ct);
        var pairs        = await _db.Pairs.CountAsync(ct);
        var groups       = await _db.Groups.CountAsync(ct);
        var openReports  = await _db.ProfileReports.CountAsync(r => !r.Resolved, ct);
        return Ok(new { users, bannedUsers, pairs, groups, openReports });
    }
}
