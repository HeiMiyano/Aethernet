using MessagePack;

namespace Aethernet.API.Dto;

/// <summary>Public profile shown to paired users — bio + picture, with NSFW flag and moderation state.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record UserProfileDto(
    UserDto User,
    bool IsFlagged,
    bool IsNsfw,
    string? Description,
    string? Base64ProfilePicture);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record UserProfileReportDto(
    UserDto ReportedUser,
    string Reason);

/// <summary>Server -> client toast / chat / popup message.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record ServerMessageDto(MessageSeverity Severity, string Message);

public enum MessageSeverity : byte
{
    Information = 0,
    Warning     = 1,
    Error       = 2,
}
