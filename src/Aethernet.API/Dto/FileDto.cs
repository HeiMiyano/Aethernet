using MessagePack;

namespace Aethernet.API.Dto;

/// <summary>Request: do you already have these blobs?</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HasFilesRequestDto(List<string> Hashes);

/// <summary>Reply: which blobs need to be uploaded, plus the size of each blob the server already
/// knows about. Sizes is keyed by hash; only present for hashes the server has stored (non-missing,
/// non-forbidden). Lets the client compute total download size BEFORE issuing per-file GETs so the
/// in-world progress bar can render a real percentage from the first byte.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HasFilesResponseDto(
    List<string> Missing,
    List<string> Forbidden,
    Dictionary<string, long>? Sizes = null);

/// <summary>Successful upload acknowledgement.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record FileUploadAckDto(string Hash, long Size, bool AlreadyExisted);

/// <summary>Reported by the file server — current quota / usage for the caller.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record FileQuotaDto(long UsedBytes, long QuotaBytes, int Files);
