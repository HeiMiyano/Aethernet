using MessagePack;

namespace Aethernet.API.Dto;

/// <summary>Reference to a group ("syncshell") by its GID.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record GroupDto(string GID)
{
    public string Alias { get; init; } = string.Empty;
    public override string ToString() => string.IsNullOrEmpty(Alias) ? GID : $"{Alias} ({GID})";
}

/// <summary>Group + the rotating password used to join. Returned only when the password was just generated.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record GroupPasswordDto(GroupDto Group, string Password);

/// <summary>Per-user info inside a group (own permissions, role).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record GroupPairUserInfoDto(
    GroupDto Group,
    UserDto User,
    GroupUserPreferredPermissions UserPermissions,
    GroupPairUserRole Role);

[Flags]
public enum GroupUserPreferredPermissions : ulong
{
    None              = 0,
    Paused            = 1 << 0,
    DisableAnimations = 1 << 1,
    DisableSounds     = 1 << 2,
    DisableVfx        = 1 << 3,
}

public enum GroupPairUserRole : byte
{
    Member    = 0,
    Moderator = 1,
    Owner     = 2,
}

/// <summary>What a member sees about a group (no other members listed).</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record GroupInfoDto(
    GroupDto Group,
    UserDto Owner,
    GroupPermissions GroupPermissions,
    int MemberCount,
    int MemberLimit);

/// <summary>Full group view — owner-only / moderator-only — includes the member list.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record GroupFullInfoDto(
    GroupDto Group,
    UserDto Owner,
    GroupPermissions GroupPermissions,
    GroupUserPreferredPermissions DefaultUserPermissions,
    List<GroupPairFullInfoDto> Members);

/// <summary>Full per-member info, visible to mods/owner.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record GroupPairFullInfoDto(
    GroupDto Group,
    UserDto User,
    GroupUserPreferredPermissions UserPermissions,
    GroupPairUserRole Role);

[Flags]
public enum GroupPermissions : ulong
{
    None                       = 0,
    DisableInvites             = 1 << 0,  // can't join even with password
    PreferDisableAnimations    = 1 << 1,
    PreferDisableSounds        = 1 << 2,
    PreferDisableVfx           = 1 << 3,
}
