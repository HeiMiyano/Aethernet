using MessagePack;

namespace Aethernet.API.Dto;

/// <summary>
/// Bit-flag permissions a user grants another user (or a group).
/// Mirrored from local UI toggles and propagated through the hub.
/// </summary>
[Flags]
public enum UserPermissions : ulong
{
    None              = 0,
    Paused            = 1 << 0,  // do not exchange data
    DisableAnimations = 1 << 1,  // strip Penumbra animations/.pap files
    DisableSounds     = 1 << 2,  // strip .scd files
    DisableVfx        = 1 << 3,  // strip .avfx files
    DisableHonorific  = 1 << 4,
    DisableMoodles    = 1 << 5,
    DisableHeels      = 1 << 6,
    DisablePetNames   = 1 << 7,
    DisableCustomize  = 1 << 8,
    Sticky            = 1 << 9,  // pair permissions override group permissions
}

[MessagePackObject(keyAsPropertyName: true)]
public sealed record UserPermissionsDto(UserDto User, UserPermissions Permissions);
