using Aethernet.Data;
using Aethernet.Shared.Observability;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.FileServer.Services;

/// <summary>
/// Two-layer disk reclaim for the blob store:
///
///   1. <b>Age-based eviction</b> — every <c>IntervalMinutes</c> we delete any blob whose
///      <c>LastTouchedAt</c> is older than <c>MaxAgeDays</c>. A blob's last-touched timestamp
///      is bumped on every download and every dedupe-hit upload, so anything that's still
///      part of an active user's mod set keeps getting refreshed. Files that haven't been
///      requested in N days are stale by definition; if a client needs one later, they re-upload.
///
///   2. <b>Disk-pressure eviction</b> — if the host disk usage crosses <c>DiskPressureHighPct</c>,
///      we evict by oldest-LastTouchedAt until we're back under <c>DiskPressureLowPct</c>.
///      This kicks in BEFORE the disk fills and bricks Postgres again.
///
/// The legacy ReferenceCount/OrphanedAt flow is left intact for admin/manual flagging but is
/// no longer used as the primary eviction signal — nothing decrements ReferenceCount in this
/// codebase, so age-based eviction is what actually keeps the disk from growing unbounded.
/// </summary>
public sealed class OrphanBlobJanitor : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _cfg;
    private readonly ILogger<OrphanBlobJanitor> _log;

    public OrphanBlobJanitor(IServiceProvider services, IConfiguration cfg, ILogger<OrphanBlobJanitor> log)
    {
        _services = services; _cfg = cfg; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval            = TimeSpan.FromMinutes(int.Parse(_cfg["Janitor:IntervalMinutes"]       ?? "15"));
        var maxAge              = TimeSpan.FromDays   (int.Parse(_cfg["Janitor:MaxAgeDays"]            ?? "14"));
        var pressureHighPct     = int.Parse(_cfg["Janitor:DiskPressureHighPct"] ?? "85");
        var pressureLowPct      = int.Parse(_cfg["Janitor:DiskPressureLowPct"]  ?? "70");
        var blobRootForFreeSpace = _cfg["BlobStore:Root"] ?? "/data/blobs";

        // Stagger the first run so multi-instance deployments don't all GC together.
        await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(30, 90)), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(maxAge, pressureHighPct, pressureLowPct, blobRootForFreeSpace, stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "janitor pass failed"); }
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunOnceAsync(
        TimeSpan maxAge, int pressureHighPct, int pressureLowPct,
        string blobRoot, CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db    = scope.ServiceProvider.GetRequiredService<AethernetDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IBlobStore>();

        // ---- Layer 1: age-based ----
        var ageCutoff = DateTime.UtcNow - maxAge;
        var stale = await db.FileCache
            .Where(f => !f.IsForbidden && f.LastTouchedAt < ageCutoff)
            .OrderBy(f => f.LastTouchedAt)
            .Take(2000)
            .ToListAsync(ct);
        if (stale.Count > 0)
        {
            long bytesFreed = 0;
            foreach (var d in stale)
            {
                try { await store.DeleteAsync(d.StorageKey, ct); bytesFreed += d.SizeBytes; }
                catch (Exception ex) { _log.LogWarning(ex, "store delete failed for {Hash}", d.Hash); }
            }
            db.FileCache.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
            _log.LogInformation("age-evicted {N} blobs (>{Days}d untouched), freed {MB:F1} MB",
                stale.Count, maxAge.TotalDays, bytesFreed / 1024.0 / 1024.0);
            AethernetMetrics.FilesOrphanGc.WithLabels("aged").Inc(stale.Count);
        }

        // ---- Layer 2: disk-pressure ----
        var (usedPct, freeBytes) = ProbeDiskUsage(blobRoot);
        if (usedPct < pressureHighPct) return;

        _log.LogWarning("disk pressure {Used}% ≥ {High}%, evicting by LastTouchedAt until {Low}%",
            usedPct, pressureHighPct, pressureLowPct);

        // Evict oldest-LastTouchedAt 500 at a time until we're back under pressureLowPct.
        // We iterate in batches because the DB query and disk delete are both expensive at scale.
        while (usedPct > pressureLowPct && !ct.IsCancellationRequested)
        {
            var oldest = await db.FileCache
                .Where(f => !f.IsForbidden)
                .OrderBy(f => f.LastTouchedAt)
                .Take(500)
                .ToListAsync(ct);
            if (oldest.Count == 0) break;

            long bytesFreed = 0;
            foreach (var d in oldest)
            {
                try { await store.DeleteAsync(d.StorageKey, ct); bytesFreed += d.SizeBytes; }
                catch (Exception ex) { _log.LogWarning(ex, "store delete failed for {Hash}", d.Hash); }
            }
            db.FileCache.RemoveRange(oldest);
            await db.SaveChangesAsync(ct);
            _log.LogInformation("pressure-evicted {N} blobs, freed {MB:F1} MB",
                oldest.Count, bytesFreed / 1024.0 / 1024.0);
            AethernetMetrics.FilesOrphanGc.WithLabels("pressure").Inc(oldest.Count);

            (usedPct, _) = ProbeDiskUsage(blobRoot);
            if (oldest.Count < 500) break;  // ran out of evictable candidates
        }
    }

    private static (int UsedPct, long FreeBytes) ProbeDiskUsage(string path)
    {
        try
        {
            // Walk up to find an existing ancestor (the path may not exist if blob store hasn't been used yet)
            var dir = new DirectoryInfo(path);
            while (dir is not null && !dir.Exists) dir = dir.Parent;
            if (dir is null) return (0, long.MaxValue);

            var drive = new DriveInfo(dir.Root.FullName);
            if (!drive.IsReady) return (0, long.MaxValue);
            var used  = drive.TotalSize - drive.TotalFreeSpace;
            var pct   = (int)(100.0 * used / drive.TotalSize);
            return (pct, drive.TotalFreeSpace);
        }
        catch { return (0, long.MaxValue); }
    }
}
