using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Configuration;
using Aethernet.Shared.Compression;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Talks to the Aethernet file server. Implements:
///  - bulk "has" queries (so we only upload blobs the server actually needs);
///  - bounded-parallelism upload of missing blobs;
///  - bounded-parallelism download of blobs needed to apply incoming character data.
/// All progress is published via events so the UI can render progress bars.
/// </summary>
public sealed class FileTransferService : IDisposable
{
    private readonly AethernetConfig _config;
    private readonly FileCacheService _cache;
    private readonly ILogger<FileTransferService> _log;
    private readonly HttpClient _http;

    private readonly ConcurrentDictionary<string, TransferProgress> _active = new();
    public event Action<TransferProgress>? ProgressChanged;
    public IReadOnlyCollection<TransferProgress> ActiveTransfers => _active.Values.ToArray();

    /// <summary>Aggregated per-UID download progress. Snapshot value: (BytesDone, BytesTotal, FilesRemaining).
    /// Used by the in-world UI overlay to render progress bars under remote characters.</summary>
    private readonly ConcurrentDictionary<string, UidDownloadProgress> _perUid = new();
    public IReadOnlyDictionary<string, UidDownloadProgress> PerUidDownloads => _perUid;
    public event Action<string>? UidDownloadChanged;  // arg = uid

    public FileTransferService(AethernetConfig config, FileCacheService cache, ILogger<FileTransferService> log)
    {
        _config = config; _cache = cache; _log = log;
        _http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer  = 16,
        })
        {
            BaseAddress = new Uri(_config.FileServerUrl, UriKind.Absolute),
            Timeout     = TimeSpan.FromMinutes(10),
        };
    }

    /// <summary>
    /// Pulls the current JWT from config on every call so token refreshes in
    /// HubConnectionService propagate automatically. Returns null if not yet logged in.
    /// </summary>
    private AuthenticationHeaderValue? BearerHeader() =>
        string.IsNullOrEmpty(_config.AccessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", _config.AccessToken);

    public async Task<HasFilesResponseDto> WhichAreMissingAsync(IReadOnlyList<string> hashes, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Routes.Files.Has)
        {
            Content = JsonContent.Create(new HasFilesRequestDto(hashes.ToList())),
        };
        req.Headers.Authorization = BearerHeader();
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<HasFilesResponseDto>(cancellationToken: ct))!;
    }

    public async Task UploadMissingAsync(IReadOnlyDictionary<string, string> hashToPath, CancellationToken ct)
    {
        var missing = await WhichAreMissingAsync(hashToPath.Keys.ToList(), ct);
        if (missing.Missing.Count == 0) return;

        var sem = new SemaphoreSlim(_config.MaxParallelUploads);
        var tasks = missing.Missing.Select(async hash =>
        {
            await sem.WaitAsync(ct);
            try { await UploadOneAsync(hash, hashToPath[hash], ct); }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task UploadOneAsync(string hash, string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        var p = _active[hash] = new TransferProgress(hash, TransferKind.Upload, info.Length);
        try
        {
            using var content = new MultipartFormDataContent
            {
                { new StringContent(hash), "hash" },
            };
            await using var fs = File.OpenRead(path);
            await using var lz = await Lz4Stream.CompressAsync(fs, ct);
            var streamContent = new StreamContent(lz);
            streamContent.Headers.ContentType     = new MediaTypeHeaderValue("application/octet-stream");
            streamContent.Headers.ContentEncoding.Add(Lz4Stream.Encoding);
            content.Add(streamContent, "file", Path.GetFileName(path));

            using var req = new HttpRequestMessage(HttpMethod.Post, Routes.Files.Upload) { Content = content };
            req.Headers.Authorization = BearerHeader();
            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            p.BytesTransferred = info.Length;
            ProgressChanged?.Invoke(p);
        }
        finally
        {
            _active.TryRemove(hash, out _);
        }
    }

    public Task DownloadManyAsync(IReadOnlyList<string> hashes, CancellationToken ct)
        => DownloadManyAsync(hashes, ownerUid: null, ct);

    /// <summary>Downloads in parallel and updates per-UID aggregated progress, so the in-world
    /// overlay can show a single bar per character regardless of how many files are in flight.</summary>
    public async Task DownloadManyAsync(IReadOnlyList<string> hashes, string? ownerUid, CancellationToken ct)
    {
        var todo = hashes.Where(h => !_cache.Has(h)).ToList();
        if (todo.Count == 0) return;

        // Ask the server up-front for sizes of every blob we're about to fetch. The server already
        // has SizeBytes in its FileCache table — one round-trip vs. N HEADs. Lets the progress UI
        // show real total-bytes from the first frame instead of growing as each file's response
        // headers arrive. Missing/forbidden entries return no size; we fall back to per-file
        // Content-Length for those (shouldn't happen in normal flow but be defensive).
        Dictionary<string, long> knownSizes = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            var ack = await WhichAreMissingAsync(todo, ct);
            if (ack.Sizes is not null)
                foreach (var (h, s) in ack.Sizes) knownSizes[h] = s;
        }
        catch (Exception ex)
        {
            _log.LogWarning("WhichAreMissingAsync (for sizes) failed, falling back to per-file size discovery: {Msg}", ex.Message);
        }
        var seedTotal = knownSizes.Values.Sum();

        // Seed per-UID progress with the pre-computed total. If two batches overlap, FilesTotal
        // and BytesTotal accumulate (don't reset).
        if (ownerUid is not null)
        {
            _perUid.AddOrUpdate(ownerUid,
                _ => new UidDownloadProgress { FilesTotal = todo.Count, FilesDone = 0, BytesDone = 0, BytesTotal = seedTotal },
                (_, existing) =>
                {
                    existing.FilesTotal += todo.Count;
                    Interlocked.Add(ref existing.BytesTotal, seedTotal);
                    return existing;
                });
            UidDownloadChanged?.Invoke(ownerUid);
        }

        var sem = new SemaphoreSlim(_config.MaxParallelDownloads);
        var tasks = todo.Select(async hash =>
        {
            await sem.WaitAsync(ct);
            // If we already have the size from /files/has, pass it so DownloadOneAsync skips the
            // "add Content-Length to BytesTotal" step (otherwise we'd double-count).
            knownSizes.TryGetValue(hash, out var preSize);
            try { await DownloadOneAsync(hash, ownerUid, sizeAlreadySeeded: preSize > 0, ct); }
            finally { sem.Release(); }
        });
        try { await Task.WhenAll(tasks); }
        finally
        {
            if (ownerUid is not null)
            {
                // Clean up when this batch is fully done (regardless of success).
                if (_perUid.TryGetValue(ownerUid, out var p) && p.FilesDone >= p.FilesTotal)
                    _perUid.TryRemove(ownerUid, out _);
                UidDownloadChanged?.Invoke(ownerUid);
            }
        }
    }

    private async Task DownloadOneAsync(string hash, string? ownerUid, bool sizeAlreadySeeded, CancellationToken ct)
    {
        var p = _active[hash] = new TransferProgress(hash, TransferKind.Download, 0);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"/files/{hash}");
            req.Headers.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue(Lz4Stream.Encoding));
            req.Headers.Authorization = BearerHeader();
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            p.TotalBytes = resp.Content.Headers.ContentLength ?? 0;
            // Only add to per-UID BytesTotal when we DIDN'T already seed it from /files/has.
            // Otherwise the size would be counted twice (once from seed, once from response header).
            if (!sizeAlreadySeeded && ownerUid is not null && _perUid.TryGetValue(ownerUid, out var seedProg))
            {
                Interlocked.Add(ref seedProg.BytesTotal, p.TotalBytes);
                UidDownloadChanged?.Invoke(ownerUid);
            }
            var rawStream = await resp.Content.ReadAsStreamAsync(ct);
            var encoded   = resp.Content.Headers.ContentEncoding.Any(e => string.Equals(e, Lz4Stream.Encoding, StringComparison.OrdinalIgnoreCase));
            await using var s = encoded ? Lz4Stream.Decompress(rawStream) : rawStream;

            var dest = _cache.GetPath(hash);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await using var fs = File.Create(dest);
            var buffer = new byte[81920];
            int n;
            long lastReported = 0;
            while ((n = await s.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                p.BytesTransferred += n;
                ProgressChanged?.Invoke(p);
                if (ownerUid is not null && _perUid.TryGetValue(ownerUid, out var prog))
                {
                    Interlocked.Add(ref prog.BytesDone, n);
                    // Throttle: only fire UI event every 64 KB to avoid storming the listeners.
                    if (p.BytesTransferred - lastReported > 65536)
                    {
                        lastReported = p.BytesTransferred;
                        UidDownloadChanged?.Invoke(ownerUid);
                    }
                }
            }
        }
        finally
        {
            _active.TryRemove(hash, out _);
            if (ownerUid is not null && _perUid.TryGetValue(ownerUid, out var prog))
            {
                Interlocked.Increment(ref prog.FilesDone);
                UidDownloadChanged?.Invoke(ownerUid);
            }
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class TransferProgress
{
    public string Hash { get; }
    public TransferKind Kind { get; }
    public long TotalBytes { get; set; }
    public long BytesTransferred { get; set; }
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Min(1.0, BytesTransferred / (double)TotalBytes);
    public TransferProgress(string hash, TransferKind kind, long total)
    { Hash = hash; Kind = kind; TotalBytes = total; }
}
public enum TransferKind { Upload, Download }

public sealed class UidDownloadProgress
{
    public int  FilesTotal;
    public int  FilesDone;
    public long BytesTotal;
    public long BytesDone;
    public double Fraction => BytesTotal <= 0 ? 0 : Math.Min(1.0, BytesDone / (double)BytesTotal);
}
