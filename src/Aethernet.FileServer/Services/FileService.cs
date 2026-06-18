using Aethernet.API.Dto;
using Aethernet.Data;
using Aethernet.Data.Entities;
using Aethernet.Shared.Compression;
using Aethernet.Shared.Hashing;
using Aethernet.Shared.Observability;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.FileServer.Services;

public sealed class FileService : IFileService
{
    private readonly AethernetDbContext _db;
    private readonly IBlobStore _store;
    private readonly IQuotaService _quota;
    private readonly ILogger<FileService> _log;

    public FileService(AethernetDbContext db, IBlobStore store, IQuotaService quota, ILogger<FileService> log)
    {
        _db = db; _store = store; _quota = quota; _log = log;
    }

    public async Task<HasFilesResponseDto> HasAsync(IReadOnlyList<string> hashes, CancellationToken ct)
    {
        var known = await _db.FileCache.AsNoTracking()
            .Where(f => hashes.Contains(f.Hash))
            .Select(f => new { f.Hash, f.IsForbidden, f.SizeBytes })
            .ToListAsync(ct);
        var knownNonForbidden = known.Where(k => !k.IsForbidden).ToList();
        var knownSet = knownNonForbidden.Select(k => k.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var forbidden = known.Where(k => k.IsForbidden).Select(k => k.Hash).ToList();
        var missing = hashes.Where(h => !knownSet.Contains(h) && !forbidden.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();

        // Sizes lets the client pre-compute total download bytes for the progress UI without
        // needing per-file HEAD requests. Only present for hashes the server actually has.
        var sizes = knownNonForbidden.ToDictionary(k => k.Hash, k => k.SizeBytes, StringComparer.OrdinalIgnoreCase);
        return new HasFilesResponseDto(missing, forbidden, sizes);
    }

    public Task<FileUploadAckDto> UploadAsync(
        string uid, string hash, Stream content, string? contentType, CancellationToken ct)
        => UploadAsync(uid, hash, content, contentType, isLz4: false, ct);

    public async Task<FileUploadAckDto> UploadAsync(
        string uid, string hash, Stream content, string? contentType, bool isLz4, CancellationToken ct)
    {
        hash = hash.ToUpperInvariant();

        var existing = await _db.FileCache.FirstOrDefaultAsync(f => f.Hash == hash, ct);
        if (existing is not null)
        {
            existing.LastTouchedAt = DateTime.UtcNow;
            existing.OrphanedAt    = null;
            existing.ReferenceCount += 1;
            await _db.SaveChangesAsync(ct);
            AethernetMetrics.FilesUploadDedupeHits.Inc();
            return new FileUploadAckDto(hash, existing.SizeBytes, AlreadyExisted: true);
        }

        // Buffer to a temp file so we can verify the hash before committing.
        var tmp = Path.GetTempFileName();
        try
        {
            long size;
            string verifiedHash;
            await using (var fs = File.Create(tmp))
            {
                if (isLz4)
                {
                    await using var dec = Lz4Stream.Decompress(content, leaveOpen: true);
                    await dec.CopyToAsync(fs, ct);
                }
                else
                {
                    await content.CopyToAsync(fs, ct);
                }
                size = fs.Length;
            }
            if (size <= 0) throw new InvalidOperationException("empty_upload");
            if (size > API.AethernetConstants.MaxFileSize) throw new InvalidOperationException("too_large");

            await using (var fs = File.OpenRead(tmp))
                verifiedHash = await Sha1Helper.HashStreamAsync(fs, ct);

            if (!string.Equals(verifiedHash, hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("hash_mismatch");

            if (!await _quota.CanAcceptAsync(uid, size, ct))
                throw new InvalidOperationException("quota_exceeded");

            await using (var fs = File.OpenRead(tmp))
                await _store.PutAsync(hash, fs, contentType, ct);

            _db.FileCache.Add(new FileCacheEntity
            {
                Hash             = hash,
                SizeBytes        = size,
                ReferenceCount   = 1,
                FirstUploaderUid = uid,
                UploadedAt       = DateTime.UtcNow,
                LastTouchedAt    = DateTime.UtcNow,
                StorageKey       = hash,
            });
            await _db.SaveChangesAsync(ct);
            AethernetMetrics.FilesUploadBytes.Inc(size);
            return new FileUploadAckDto(hash, size, AlreadyExisted: false);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    public async Task<(FileCacheEntity Entry, Stream Stream)> DownloadAsync(
        string hash, long? offset, long? length, CancellationToken ct)
    {
        var entry = await _db.FileCache.FirstOrDefaultAsync(f => f.Hash == hash.ToUpperInvariant(), ct)
                    ?? throw new FileNotFoundException();
        if (entry.IsForbidden) throw new InvalidOperationException("forbidden");
        var stream = await _store.GetAsync(entry.StorageKey, offset, length, ct);
        entry.LastTouchedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        AethernetMetrics.FilesDownloadBytes.Inc(length ?? entry.SizeBytes);
        return (entry, stream);
    }

    public async Task DeleteAsync(string uid, string hash, bool isAdmin, CancellationToken ct)
    {
        var entry = await _db.FileCache.FirstOrDefaultAsync(f => f.Hash == hash.ToUpperInvariant(), ct);
        if (entry is null) return;
        if (!isAdmin && entry.FirstUploaderUid != uid)
            throw new UnauthorizedAccessException();
        await _store.DeleteAsync(entry.StorageKey, ct);
        _db.FileCache.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }
}
