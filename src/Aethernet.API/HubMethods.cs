namespace Aethernet.API;

/// <summary>
/// Method-name constants for the SignalR hub.
/// Server methods are invoked by clients; client methods are invoked by the server on connected clients.
/// </summary>
public static class HubMethods
{
    /// <summary>Methods exposed by <c>AethernetHub</c> on the server (callable by clients).</summary>
    public static class Server
    {
        // --- session ---
        public const string Heartbeat                = "Heartbeat";
        public const string CheckClientHealth        = "CheckClientHealth";
        public const string UserSetIdent             = "UserSetIdent";  // publish "Name@WorldID" for visibility

        // --- user / account ---
        public const string UserGetOnlinePairs       = "UserGetOnlinePairs";
        public const string UserGetPairedClients     = "UserGetPairedClients";
        public const string UserDelete               = "UserDelete";
        public const string UserGetProfile           = "UserGetProfile";
        public const string UserSetProfile           = "UserSetProfile";
        public const string UserReportProfile        = "UserReportProfile";

        // --- pairing ---
        public const string UserAddPair              = "UserAddPair";
        public const string UserRemovePair           = "UserRemovePair";
        public const string UserSetPairPermissions   = "UserSetPairPermissions";
        public const string UserBlock                = "UserBlock";
        public const string UserUnblock              = "UserUnblock";
        public const string UserGetBlocked           = "UserGetBlocked";

        // --- character data ---
        public const string UserPushData             = "UserPushData";
        public const string UserRequestCharacterData = "UserRequestCharacterData";

        // --- groups (syncshells) ---
        public const string GroupCreate              = "GroupCreate";
        public const string GroupCreateWithPassword  = "GroupCreateWithPassword";
        public const string GroupJoin                = "GroupJoin";
        public const string GroupLeave               = "GroupLeave";
        public const string GroupDelete              = "GroupDelete";
        public const string GroupChangePassword      = "GroupChangePassword";
        public const string GroupSetPermissions      = "GroupSetPermissions";
        public const string GroupChangeOwnership     = "GroupChangeOwnership";
        public const string GroupChangeUserInfo      = "GroupChangeUserInfo";
        public const string GroupRemoveUser          = "GroupRemoveUser";
        public const string GroupBanUser             = "GroupBanUser";
        public const string GroupUnbanUser           = "GroupUnbanUser";
        public const string GroupGetBans             = "GroupGetBans";
        public const string GroupClearAll            = "GroupClearAll";
        public const string GroupSetUserModerator    = "GroupSetUserModerator";
        public const string GroupSetAlias            = "GroupSetAlias";

        // --- moderation (server-admin gated) ---
        public const string ModerationBanUser        = "ModerationBanUser";
        public const string ModerationUnbanUser      = "ModerationUnbanUser";
        public const string ModerationGetReports     = "ModerationGetReports";
    }

    /// <summary>Methods the server invokes on connected clients (declared by the plugin).</summary>
    public static class Client
    {
        // --- session ---
        public const string ReceiveServerMessage     = "Client_ReceiveServerMessage";
        public const string ForceClientUpdate        = "Client_ForceClientUpdate";

        // --- presence ---
        public const string UserSendOnline           = "Client_UserSendOnline";
        public const string UserSendOffline          = "Client_UserSendOffline";

        // --- pairing ---
        public const string UserAddPair              = "Client_UserAddPair";
        public const string UserRemovePair           = "Client_UserRemovePair";
        public const string UserUpdatePairPermissions= "Client_UserUpdatePairPermissions";
        public const string UserUpdateProfile        = "Client_UserUpdateProfile";

        // --- character data ---
        public const string UserReceiveCharacterData = "Client_UserReceiveCharacterData";

        // --- groups ---
        public const string GroupSendFullInfo        = "Client_GroupSendFullInfo";
        public const string GroupSendInfo            = "Client_GroupSendInfo";
        public const string GroupDelete              = "Client_GroupDelete";
        public const string GroupPairJoined          = "Client_GroupPairJoined";
        public const string GroupPairLeft            = "Client_GroupPairLeft";
        public const string GroupPairChangeUserInfo  = "Client_GroupPairChangeUserInfo";
    }
}
