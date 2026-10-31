using MessagePack;

namespace Aethernet.API.Dto;

/// <summary>Values for <see cref="DiscordBeginRequestDto.Mode"/>.</summary>
public static class DiscordAuthModes
{
    /// <summary>Create a new account, or sign in to the account already linked to this Discord user
    /// (which issues a fresh secret key — this doubles as "new PC" and "lost my key").</summary>
    public const string SignIn = "signin";

    /// <summary>Attach Discord to an existing pre-Discord account. Requires the account's UID + secret key.</summary>
    public const string Link = "link";
}

/// <summary>Values for <see cref="DiscordPollResponseDto.Status"/>.</summary>
public static class DiscordPollStatus
{
    public const string Pending  = "pending";
    public const string Complete = "complete";
    public const string Failed   = "failed";
}

/// <summary>Machine-readable error codes returned in <see cref="AuthErrorDto.Error"/>.</summary>
public static class AuthErrorCodes
{
    public const string DiscordLinkRequired  = "discord_link_required";
    public const string DiscordNotConfigured = "discord_not_configured";
    public const string RegistrationDisabled = "registration_disabled";
    public const string InvalidCredentials   = "invalid_credentials";
    public const string AlreadyLinked        = "already_linked";
    public const string Banned               = "banned";
    public const string Busy                 = "busy";
    public const string BadRequest           = "bad_request";
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed record AuthErrorDto(string Error, string Message);

/// <summary>Plugin → auth: start a browser Discord sign-in. UID + SecretKey only for <see cref="DiscordAuthModes.Link"/>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record DiscordBeginRequestDto(string Mode, string? UID, string? SecretKey);

/// <summary>Auth → plugin: open <see cref="AuthorizeUrl"/> in the browser, then poll with <see cref="PollToken"/>.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record DiscordBeginResponseDto(string AuthorizeUrl, string PollToken, int ExpiresInSeconds);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record DiscordPollRequestDto(string PollToken);

/// <summary>
/// Result of a browser sign-in. On <see cref="DiscordPollStatus.Complete"/> after a sign-in, UID +
/// SecretKey are the credentials to store (delivered exactly once — the server forgets them after
/// this response). After a link, SecretKey is null: the existing key stays valid.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record DiscordPollResponseDto(
    string Status, string? UID, string? SecretKey, bool ExistingAccount, string? Error, string? Message);
