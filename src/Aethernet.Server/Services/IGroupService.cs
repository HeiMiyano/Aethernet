using Aethernet.API.Dto;

namespace Aethernet.Server.Services;

public interface IGroupService
{
    Task<GroupPasswordDto>   CreateAsync(string ownerUid);
    Task<GroupFullInfoDto>   JoinAsync(string uid, GroupPasswordDto join);
    Task                     LeaveAsync(string uid, GroupDto group);
    Task                     DeleteAsync(string uid, GroupDto group);
    Task<GroupPasswordDto>   RotatePasswordAsync(string uid, GroupDto group);
    Task                     SetPermissionsAsync(string uid, GroupDto group, GroupPermissions perms);
    Task                     ChangeOwnershipAsync(string uid, GroupDto group, UserDto newOwner);
    Task                     SetUserPermissionsAsync(string uid, GroupPairUserInfoDto info);
    Task                     RemoveUserAsync(string uid, GroupDto group, UserDto user);
    Task                     BanUserAsync(string uid, GroupDto group, UserDto user, string? reason);
    Task                     UnbanUserAsync(string uid, GroupDto group, UserDto user);
    Task<List<UserDto>>      GetBansAsync(string uid, GroupDto group);
    Task                     ClearAsync(string uid, GroupDto group);
    Task                     SetModeratorAsync(string uid, GroupDto group, UserDto user, bool isMod);
    Task                     SetAliasAsync(string uid, GroupDto group, string? alias);
}
