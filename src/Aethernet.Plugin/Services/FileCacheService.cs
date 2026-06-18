using Aethernet.Plugin.Configuration;
using Aethernet.Shared.Hashing;
using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Local disk cache for mod blobs we've uploaded or downloaded. Files are stored under
/// %AppData%\Aethernet\cache\{hash[0..2]}\{hash[2..4]}\{hash} so a single directory never gets
/// too many entries. LRU-style eviction is driven by the file mtime.
/// </summary>
public sealed class FileCacheService
{
    private readonly AethernetConfig _config;
    private readonly ILogger<FileCacheService> _log;
    private readonly string _root;

    public FileCacheService(AethernetConfig config, IDalamudPluginInterface pi, ILogger<FileCacheService> log)
    {
        _config = config; _log = log;
        _root = string.IsNullOrWhiteSpace(config.FileCacheDirectory)
            ? Path.Combine(pi.ConfigDirectory.FullName, "cache")
            : config.FileCacheDirectory;
        Directory.CreateDirectory(_root);
    }

    private string PathFor(string hash) =>
        Path.Combine(_root, hash[..2], hash[2..4], hash);

    public bool Has(string hash) => File.Exists(PathFor(hash));

    /// <summary>Returns the extension-less cache path for a hash. Use the <paramref name="gamePath"/>
    /// overload when handing the path to Penumbra — it requires file extensions to match.</summary>
    public string GetPath(string hash) => PathFor(hash);

    /// <summary>Returns a cache path with the same extension as <paramref name="gamePath"/>.
    /// Penumbra rejects file replacements whose source extension doesn't match the destination
    /// game path's extension, so cache files must be exposed with the right extension.
    /// On first use we hardlink (or copy as a fallback) the canonical hash file to an
    /// extensioned twin so subsequent lookups are O(1) file-exists checks.</summary>
    public string GetPath(string hash, string gamePath)
    {
        var basePath = PathFor(hash);
        var ext = Path.GetExtension(gamePath);
        if (string.IsNullOrEmpty(ext)) return basePath;
        var extensioned = basePath + ext;
        if (File.Exists(extensioned)) return extensioned;
        if (!File.Exists(basePath)) return extensioned;  // returns soon-to-exist path; missing-file logging handles this elsewhere
        try
        {
            // Hardlink is instant and shares disk space; fall back to a regular copy on filesystems
            // that don't support it (FAT32, some network drives) or if the file is already linked.
            try { Microsoft.Win32.SafeHandles.SafeFileHandle? _ = null; CreateHardLink(extensioned, basePath, IntPtr.Zero); }
            catch { /* fall through */ }
            if (!File.Exists(extensioned))
                File.Copy(basePath, extensioned, overwrite: false);
        }
        catch (IOException) { /* race-condition retry: another caller created it first */ }
        catch (Exception ex) { _log.LogWarning("Failed to extension-ify cache file {Base} -> {Ext}: {Msg}", basePath, extensioned, ex.Message); }
        return extensioned;
    }

    [System.Runtime.InteropServices.DllImport("Kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    public string Store(string hash, byte[] bytes)
    {
        var path = PathFor(hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public string StoreStream(string hash, Stream stream)
    {
        var path = PathFor(hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        stream.CopyTo(fs);
        return path;
    }

    // (path, mtimeTicks, size) -> sha1 hash. Avoids re-hashing the same file every sync cycle.
    // Cleared on mtime/size change, never invalidates if the file is untouched.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Mtime, long Size, string Hash)> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hashes a file and copies it into the cache under its hash. Returns the (hash, cachePath).
    /// Subsequent calls for the same unchanged file return the cached hash without re-reading
    /// the file (critical for multi-GB mod folders).</summary>
    public (string Hash, string CachePath) Ingest(string sourcePath)
    {
        var info = new FileInfo(sourcePath);
        var mtime = info.LastWriteTimeUtc.Ticks;
        var size  = info.Length;
        string hash;

        if (_hashCache.TryGetValue(sourcePath, out var cached) && cached.Mtime == mtime && cached.Size == size)
        {
            hash = cached.Hash;
        }
        else
        {
            hash = Sha1Helper.HashFile(sourcePath);
            _hashCache[sourcePath] = (mtime, size, hash);
        }

        var dest = PathFor(hash);
        if (!File.Exists(dest))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(sourcePath, dest, overwrite: false);
        }
        else
        {
            File.SetLastWriteTimeUtc(dest, DateTime.UtcNow);
        }
        return (hash, dest);
    }

    /// <summary>Evict oldest files until the cache is under the configured limit.</summary>
    public void EnforceQuota()
    {
        var files = new DirectoryInfo(_root).EnumerateFiles("*", SearchOption.AllDirectories)
            .OrderBy(f => f.LastAccessTimeUtc)
            .ToList();
        long total = files.Sum(f => f.Length);
        var limit = _config.MaxCacheSizeBytes;
        foreach (var f in files)
        {
            if (total <= limit) break;
            try { total -= f.Length; f.Delete(); } catch (Exception ex) { _log.LogDebug(ex, "cache evict failed for {Path}", f.FullName); }
        }
    }
}
