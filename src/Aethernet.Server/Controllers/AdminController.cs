using System.Security.Claims;
using Aethernet.API.Dto;
using Aethernet.Data;
using Aethernet.Data.Entities;
using Aethernet.Server.Hubs;
using Aethernet.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
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
    private readonly IBanEnforcementService _banEnforcement;
    private readonly IPresenceTracker _presence;
    private readonly MetricsSampler _sampler;
    private readonly IHubContext<AethernetHub, IAethernetHubClient> _hub;

    public AdminController(
        AethernetDbContext db, IBanEnforcementService banEnforcement, IPresenceTracker presence,
        MetricsSampler sampler, IHubContext<AethernetHub, IAethernetHubClient> hub)
    {
        _db = db; _banEnforcement = banEnforcement; _presence = presence; _sampler = sampler; _hub = hub;
    }

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
        var memberships  = await _db.GroupPairs.AsNoTracking()
            .Where(g => g.Uid == uid)
            .Select(g => new { gid = g.Gid, alias = g.Group!.Alias, role = g.Role.ToString(), joinedAt = g.JoinedAt })
            .ToListAsync(ct);
        var online = await _presence.IsOnlineAsync(uid);
        return Ok(new
        {
            uid          = u.Uid,
            alias        = u.Alias,
            discordUserId = u.DiscordUserId,
            discordUsername    = u.DiscordUsername,
            discordDisplayName = u.DiscordDisplayName,
            discordProfileUpdatedAt = u.DiscordProfileUpdatedAt,
            isAdmin      = u.IsAdmin,
            isModerator  = u.IsModerator,
            isBanned     = u.IsBanned,
            banReason    = u.BanReason,
            createdAt    = u.CreatedAt,
            lastSeenAt   = u.LastSeenAt,
            lastLoginAt  = u.LastLoginAt,
            online,
            ident        = online ? await _presence.GetIdentAsync(uid) : null,
            pairs, groups, openReports = reportsOpen,
            memberships,
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
        await _banEnforcement.EnforceBanAsync(uid, req.Reason);
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

    // ---- overview / metrics -------------------------------------------------------------

    /// <summary>Everything the Overview tab draws: headline counts from Postgres, signups per day
    /// (30d, from Users.CreatedAt), and live traffic from <see cref="MetricsSampler"/> (24h max,
    /// resets when the hub restarts).</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview([FromQuery] int bucketMinutes = 15, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var since30 = now.Date.AddDays(-29);

        var signupRows = await _db.Users.AsNoTracking()
            .Where(u => u.CreatedAt >= since30)
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var signupsByDay = signupRows.ToDictionary(r => r.Day, r => r.Count);
        var signups = Enumerable.Range(0, 30)
            .Select(i => since30.AddDays(i))
            .Select(d => new { day = d.ToString("yyyy-MM-dd"), count = signupsByDay.GetValueOrDefault(d) })
            .ToList();

        var liveFiles = _db.FileCache.AsNoTracking().Where(f => f.OrphanedAt == null);
        var fileCount = await liveFiles.CountAsync(ct);
        var fileBytes = await liveFiles.SumAsync(f => (long?)f.SizeBytes, ct) ?? 0L;

        return Ok(new
        {
            generatedAt   = now,
            onlineNow     = await _presence.OnlineCountAsync(),
            users         = await _db.Users.CountAsync(ct),
            discordLinked = await _db.Users.CountAsync(u => u.DiscordUserId != null, ct),
            banned        = await _db.Users.CountAsync(u => u.IsBanned, ct),
            staff         = await _db.Users.CountAsync(u => u.IsAdmin || u.IsModerator, ct),
            active1d      = await _db.Users.CountAsync(u => u.LastSeenAt >= now.AddDays(-1), ct),
            active7d      = await _db.Users.CountAsync(u => u.LastSeenAt >= now.AddDays(-7), ct),
            active30d     = await _db.Users.CountAsync(u => u.LastSeenAt >= now.AddDays(-30), ct),
            groups        = await _db.Groups.CountAsync(ct),
            pairs         = await _db.Pairs.CountAsync(ct),
            openReports   = await _db.ProfileReports.CountAsync(r => !r.Resolved, ct),
            files         = fileCount,
            fileBytes,
            signupsPerDay = signups,
            traffic = new
            {
                bucketMinutes = Math.Clamp(bucketMinutes, 1, 60),
                samplingSince = _sampler.StartedAt,
                sources       = _sampler.SourceStatus,
                buckets       = _sampler.Buckets(Math.Clamp(bucketMinutes, 1, 60)),
            },
        });
    }

    // ---- user browser -------------------------------------------------------------------

    [HttpGet("users")]
    public async Task<IActionResult> Users(
        [FromQuery] string? q = null, [FromQuery] string filter = "all", [FromQuery] string sort = "lastSeen",
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 10, 200);
        page = Math.Max(1, page);
        var onlineUids = await _presence.OnlineUidsAsync();

        var query = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var like = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            // "@name" searches Discord usernames the way people usually type them.
            var handle = term.TrimStart('@');
            var handleLike = "%" + handle.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(u =>
                EF.Functions.ILike(u.Uid, like) ||
                (u.Alias != null && EF.Functions.ILike(u.Alias, like)) ||
                u.DiscordUserId == term ||
                (u.DiscordUsername != null && EF.Functions.ILike(u.DiscordUsername, handleLike)) ||
                (u.DiscordDisplayName != null && EF.Functions.ILike(u.DiscordDisplayName, like)));
        }
        query = filter switch
        {
            "online"   => query.Where(u => onlineUids.Contains(u.Uid)),
            "banned"   => query.Where(u => u.IsBanned),
            "staff"    => query.Where(u => u.IsAdmin || u.IsModerator),
            "unlinked" => query.Where(u => u.DiscordUserId == null),
            "linked"   => query.Where(u => u.DiscordUserId != null),
            _          => query,
        };
        query = sort switch
        {
            "created" => query.OrderByDescending(u => u.CreatedAt),
            "uid"     => query.OrderBy(u => u.Uid),
            "alias"   => query.OrderBy(u => u.Alias == null).ThenBy(u => u.Alias),
            "discord" => query.OrderBy(u => u.DiscordUsername == null).ThenBy(u => u.DiscordUsername),
            _         => query.OrderByDescending(u => u.LastSeenAt),
        };

        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(u => new
            {
                uid = u.Uid, alias = u.Alias, discordUserId = u.DiscordUserId,
                discordUsername = u.DiscordUsername, discordDisplayName = u.DiscordDisplayName,
                isAdmin = u.IsAdmin, isModerator = u.IsModerator, isBanned = u.IsBanned,
                createdAt = u.CreatedAt, lastSeenAt = u.LastSeenAt,
            })
            .ToListAsync(ct);
        var online = onlineUids.ToHashSet();
        return Ok(new
        {
            total, page, pageSize,
            rows = rows.Select(r => new
            {
                r.uid, r.alias, r.discordUserId, r.discordUsername, r.discordDisplayName, r.isAdmin, r.isModerator, r.isBanned, r.createdAt, r.lastSeenAt,
                online = online.Contains(r.uid),
            }),
        });
    }

    [HttpGet("online")]
    public async Task<IActionResult> Online(CancellationToken ct)
    {
        var uids = await _presence.OnlineUidsAsync();
        var users = await _db.Users.AsNoTracking()
            .Where(u => uids.Contains(u.Uid))
            .Select(u => new { u.Uid, u.Alias, u.DiscordUserId, u.DiscordUsername, u.DiscordDisplayName, u.IsBanned, u.LastSeenAt })
            .ToListAsync(ct);
        var rows = new List<object>(users.Count);
        foreach (var u in users.OrderByDescending(u => u.LastSeenAt))
            rows.Add(new
            {
                uid = u.Uid, alias = u.Alias, discordLinked = u.DiscordUserId != null,
                discordUsername = u.DiscordUsername, discordDisplayName = u.DiscordDisplayName, isBanned = u.IsBanned,
                connectedAt = u.LastSeenAt, ident = await _presence.GetIdentAsync(u.Uid),
            });
        return Ok(rows);
    }

    // ---- admin-only account actions -------------------------------------------------------

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "admin")]
    [HttpPost("users/{uid}/unlink-discord")]
    public async Task<IActionResult> UnlinkDiscord(string uid, CancellationToken ct)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Uid == uid, ct);
        if (u is null) return NotFound();
        var old = u.DiscordUserId;
        u.DiscordUserId = null;
        u.DiscordUsername = null;
        u.DiscordDisplayName = null;
        u.DiscordProfileUpdatedAt = null;
        _db.AuditLog.Add(new AuditLogEntity
        {
            ActorUid = Uid, Action = "user.discord_unlink", TargetUid = uid, Detail = old, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public record RoleRequest(bool IsModerator);

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "admin")]
    [HttpPost("users/{uid}/moderator")]
    public async Task<IActionResult> SetModerator(string uid, [FromBody] RoleRequest req, CancellationToken ct)
    {
        var u = await _db.Users.FirstOrDefaultAsync(x => x.Uid == uid, ct);
        if (u is null) return NotFound();
        u.IsModerator = req.IsModerator;
        _db.AuditLog.Add(new AuditLogEntity
        {
            ActorUid = Uid, Action = req.IsModerator ? "user.mod_grant" : "user.mod_revoke", TargetUid = uid,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---- syncshells -----------------------------------------------------------------------

    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery] string? q = null, CancellationToken ct = default)
    {
        var query = _db.Groups.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var like = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(g => EF.Functions.ILike(g.Gid, like) ||
                                     (g.Alias != null && EF.Functions.ILike(g.Alias, like)) ||
                                     g.OwnerUid == term ||
                                     (g.Owner!.DiscordUsername != null && EF.Functions.ILike(g.Owner!.DiscordUsername, like)));
        }
        var rows = await query
            .Select(g => new
            {
                gid = g.Gid, alias = g.Alias, ownerUid = g.OwnerUid, ownerAlias = g.Owner!.Alias,
                ownerDiscordUsername = g.Owner!.DiscordUsername,
                members = g.Members.Count, memberLimit = g.MemberLimit, createdAt = g.CreatedAt,
            })
            .OrderByDescending(g => g.members).ThenBy(g => g.gid)
            .Take(500)
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("groups/{gid}")]
    public async Task<IActionResult> Group(string gid, CancellationToken ct)
    {
        var g = await _db.Groups.AsNoTracking().FirstOrDefaultAsync(x => x.Gid == gid, ct);
        if (g is null) return NotFound();
        var members = await _db.GroupPairs.AsNoTracking()
            .Where(m => m.Gid == gid)
            .OrderByDescending(m => m.Role).ThenBy(m => m.JoinedAt)
            .Select(m => new { uid = m.Uid, alias = m.User!.Alias, discordUsername = m.User!.DiscordUsername, role = m.Role.ToString(), joinedAt = m.JoinedAt })
            .ToListAsync(ct);
        var bans = await _db.GroupBans.CountAsync(b => b.Gid == gid, ct);
        return Ok(new
        {
            gid = g.Gid, alias = g.Alias, ownerUid = g.OwnerUid, createdAt = g.CreatedAt,
            memberLimit = g.MemberLimit, bans, members,
        });
    }

    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "admin")]
    [HttpDelete("groups/{gid}")]
    public async Task<IActionResult> DeleteGroup(string gid, CancellationToken ct)
    {
        var g = await _db.Groups.FirstOrDefaultAsync(x => x.Gid == gid, ct);
        if (g is null) return NotFound();
        var members = await _db.GroupPairs.Where(m => m.Gid == gid).Select(m => m.Uid).ToListAsync(ct);
        _db.Groups.Remove(g);
        _db.AuditLog.Add(new AuditLogEntity
        {
            ActorUid = Uid, Action = "group.delete", TargetGid = gid,
            Detail = $"{g.Alias ?? "(no alias)"}, owner {g.OwnerUid}, {members.Count} members", CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        // Same notification GroupService.DeleteAsync sends, so members' plugins drop it live.
        var dto = new GroupDto(gid);
        foreach (var m in members)
            await _hub.Clients.User(m).Client_GroupDelete(dto);
        return NoContent();
    }
}
