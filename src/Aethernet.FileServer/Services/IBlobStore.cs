namespace Aethernet.FileServer.Services;

/// <summary>Abstraction over the underlying blob backend (S3 / disk / etc.).</summary>
public interface IBlobStore
{
    Task<bool> ExistsAsync(string key, CancellationToken ct);
    Task<long> PutAsync(string key, Stream content, string? contentType, CancellationToken ct);
    Task<Stream> GetAsync(string key, long? offset, long? length, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
