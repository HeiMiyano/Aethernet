using System.Security.Claims;
using Aethernet.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.FileServer.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "admin,moderator")]
[Route("admin/files")]
public sealed class AdminController : ControllerBase
{
    private readonly AethernetDbContext _db;
    public AdminController(AethernetDbContext db) { _db = db; }

    private string Uid => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public record ForbidRequest(string Hash, string? Reason);

    [HttpPost("forbid")]
    public async Task<IActionResult> Forbid([FromBody] ForbidRequest req, CancellationToken ct)
    {
        var hash = req.Hash.ToUpperInvariant();
        var row = await _db.FileCache.FirstOrDefaultAsync(f => f.Hash == hash, ct);
        if (row is null) return NotFound();
        row.IsForbidden = true; row.ForbiddenReason = req.Reason;
        _db.AuditLog.Add(new Data.Entities.AuditLogEntity
        {
            ActorUid = Uid, Action = "files.forbid", Detail = $"{hash}: {req.Reason}", CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("allow")]
    public async Task<IActionResult> Allow([FromBody] ForbidRequest req, CancellationToken ct)
    {
        var hash = req.Hash.ToUpperInvariant();
        var row = await _db.FileCache.FirstOrDefaultAsync(f => f.Hash == hash, ct);
        if (row is null) return NotFound();
        row.IsForbidden = false; row.ForbiddenReason = null;
        _db.AuditLog.Add(new Data.Entities.AuditLogEntity
        {
            ActorUid = Uid, Action = "files.allow", Detail = hash, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var rows = await _db.FileCache.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                count = g.Count(),
                totalBytes = g.Sum(x => x.SizeBytes),
                orphans = g.Count(x => x.OrphanedAt != null),
                forbidden = g.Count(x => x.IsForbidden),
            })
            .FirstOrDefaultAsync(ct);
        return Ok(rows ?? new { count = 0, totalBytes = 0L, orphans = 0, forbidden = 0 });
    }
}
