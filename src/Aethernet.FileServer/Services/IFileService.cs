using Aethernet.API.Dto;
using Aethernet.Data.Entities;

namespace Aethernet.FileServer.Services;

public interface IFileService
{
    Task<HasFilesResponseDto> HasAsync(IReadOnlyList<string> hashes, CancellationToken ct);
    Task<FileUploadAckDto> UploadAsync(string uid, string hash, Stream content, string? contentType, CancellationToken ct);
    Task<FileUploadAckDto> UploadAsync(string uid, string hash, Stream content, string? contentType, bool isLz4, CancellationToken ct);
    Task<(FileCacheEntity Entry, Stream Stream)> DownloadAsync(string hash, long? offset, long? length, CancellationToken ct);
    Task DeleteAsync(string uid, string hash, bool isAdmin, CancellationToken ct);
}
