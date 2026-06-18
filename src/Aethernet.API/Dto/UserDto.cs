using MessagePack;

namespace Aethernet.API.Dto;

/// <summary>Stable, opaque user identifier. Format: 8–24 chars, base32-ish.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record UserDto(string UID)
{
    public string Alias { get; init; } = string.Empty;
    public override string ToString() => string.IsNullOrEmpty(Alias) ? UID : $"{Alias} ({UID})";
}

/// <summary>A user paired with the local user.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record UserPairDto(
    UserDto User,
    IndividualPairStatus IndividualPairStatus,
    UserPermissions OwnPermissions,
    UserPermissions OtherPermissions)
{
    /// <summary>Group IDs both users share. Pair stays alive while at least one group remains.</summary>
    public List<string> SharedGroups { get; init; } = new();
}

/// <summary>Identifier returned when a paired user comes online (so we know their player name to look for).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record OnlineUserIdentDto(UserDto User, string Ident);

/// <summary>State of a one-to-one pair request.</summary>
public enum IndividualPairStatus : byte
{
    None      = 0,
    OneSided  = 1, // we added them, they haven't added us back
    Bidirectional = 2,
}
