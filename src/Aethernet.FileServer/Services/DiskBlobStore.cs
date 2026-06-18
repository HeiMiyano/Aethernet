namespace Aethernet.FileServer.Services;

/// <summary>Disk-backed blob store for tiny single-node deployments. Lays out hashes in 2-char fanout dirs.</summary>
public sealed class DiskBlobStore : IBlobStore
{
    private readonly string _root;
    public DiskBlobStore(IConfiguration cfg)
    {
        _root = cfg["Storage:Path"] ?? Path.Combine(AppContext.BaseDirectory, "blobs");
        Directory.CreateDirectory(_root);
    }

    private string PathFor(string key)
    {
        if (key.Length < 4) throw new ArgumentException("key too short");
        var dir = Path.Combine(_root, key[..2], key[2..4]);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, key);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct)
        => Task.FromResult(File.Exists(PathFor(key)));

    public async Task<long> PutAsync(string key, Stream content, string? contentType, CancellationToken ct)
    {
        var path = PathFor(key);
        var tmp  = path + ".tmp";
        await using (var fs = File.Create(tmp))
            await content.CopyToAsync(fs, ct);
        if (File.Exists(path)) File.Delete(tmp); else File.Move(tmp, path);
        return new FileInfo(path).Length;
    }

    public Task<Stream> GetAsync(string key, long? offset, long? length, CancellationToken ct)
    {
        var path = PathFor(key);
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (offset is not null) fs.Seek(offset.Value, SeekOrigin.Begin);
        return Task.FromResult<Stream>(length is not null
            ? new BoundedReadStream(fs, length.Value)
            : fs);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>Wraps a stream so it stops returning bytes after <c>limit</c>.</summary>
    private sealed class BoundedReadStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;
        public BoundedReadStream(Stream inner, long limit) { _inner = inner; _remaining = limit; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var toRead = (int)Math.Min(count, _remaining);
            var n = _inner.Read(buffer, offset, toRead);
            _remaining -= n;
            return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() {}
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
