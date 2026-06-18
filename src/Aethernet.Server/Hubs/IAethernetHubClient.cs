using Aethernet.API.Dto;

namespace Aethernet.Server.Hubs;

/// <summary>
/// Strongly-typed view of the methods we invoke on connected clients. For SignalR's strongly-typed
/// hubs, the C# method name IS the wire method name — so these names must match the constants in
/// <see cref="Aethernet.API.HubMethods.Client"/> verbatim (Client_-prefixed so logs make
/// direction obvious).
/// </summary>
public interface IAethernetHubClient
{
    Task Client_ReceiveServerMessage(ServerMessageDto message);
    Task Client_ForceClientUpdate(string reason);

    Task Client_UserSendOnline(OnlineUserIdentDto ident);
    Task Client_UserSendOffline(UserDto user);

    Task Client_UserAddPair(UserPairDto pair);
    Task Client_UserRemovePair(string uid);
    Task Client_UserUpdatePairPermissions(UserPermissionsDto perms);
    Task Client_UserUpdateProfile(UserProfileDto profile);

    Task Client_UserReceiveCharacterData(OnlineUserCharaDataMessageDto data);

    Task Client_GroupSendFullInfo(GroupFullInfoDto info);
    Task Client_GroupSendInfo(GroupInfoDto info);
    Task Client_GroupDelete(GroupDto group);
    Task Client_GroupPairJoined(GroupPairFullInfoDto pair);
    Task Client_GroupPairLeft(GroupDto group, UserDto user);
    Task Client_GroupPairChangeUserInfo(GroupPairUserInfoDto info);
}
