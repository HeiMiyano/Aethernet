using MessagePack;

namespace Aethernet.API.Dto;

[MessagePackObject(keyAsPropertyName: true)]
public sealed record RegisterRequestDto(string? RecoverySecret);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record RegisterResponseDto(string UID, string SecretKey);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record LoginRequestDto(string UID, string SecretKey);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record LoginResponseDto(string AccessToken, string RefreshToken, int ExpiresInSeconds);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record RefreshRequestDto(string RefreshToken);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MeResponseDto(string UID, string? Alias, bool IsAdmin, bool IsModerator, DateTime CreatedAt);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record ServerInfoDto(int ProtocolVersion, string Build, string? Motd, int OnlineUsers);
