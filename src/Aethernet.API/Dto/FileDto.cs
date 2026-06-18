using MessagePack;

namespace Aethernet.API.Dto;

/// <summary>Request: do you already have these blobs?</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HasFilesRequestDto(List<string> Hashes);

/// <summary>Reply: which blobs need to be uploaded.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record HasFilesResponseDto(List<string> Missing, List<string> Forbidden);

/// <summary>Successful upload acknowledgement.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record FileUploadAckDto(string Hash, long Size, bool AlreadyExisted);

/// <summary>Reported by the file server — current quota / usage for the caller.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record FileQuotaDto(long UsedBytes, long QuotaBytes, int Files);
