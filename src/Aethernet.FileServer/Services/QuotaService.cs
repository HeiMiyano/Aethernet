using Aethernet.Data;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.FileServer.Services;

public sealed class QuotaService : IQuotaService
{
    private readonly AethernetDbContext _db;
    private readonly IConfiguration _cfg;
    public QuotaService(AethernetDbContext db, IConfiguration cfg) { _db = db; _cfg = cfg; }

    public async Task<(long Used, long Quota, int Files)> GetForUserAsync(string uid, CancellationToken ct)
    {
        var stats = await _db.FileCache.AsNoTracking()
            .Where(f => f.FirstUploaderUid == uid)
            .GroupBy(_ => 1)
            .Select(g => new { Used = g.Sum(x => x.SizeBytes), Files = g.Count() })
            .FirstOrDefaultAsync(ct);

        var perUser = await _db.Users.AsNoTracking()
            .Where(u => u.Uid == uid).Select(u => u.FileQuotaBytes).FirstOrDefaultAsync(ct);
        var defaultQuota = long.Parse(_cfg["Storage:DefaultQuotaBytes"] ?? (5L * 1024 * 1024 * 1024).ToString());
        return (stats?.Used ?? 0, perUser ?? defaultQuota, stats?.Files ?? 0);
    }

    public async Task<bool> CanAcceptAsync(string uid, long incomingBytes, CancellationToken ct)
    {
        var (used, quota, _) = await GetForUserAsync(uid, ct);
        return used + incomingBytes <= quota;
    }
}
