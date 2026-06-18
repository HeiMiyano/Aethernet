using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Data;
using Aethernet.Data.Entities;
using Aethernet.Server.Hubs;
using Aethernet.Shared.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.Server.Services;

public sealed class GroupService : IGroupService
{
    private readonly AethernetDbContext _db;
    private readonly IHubContext<AethernetHub, IAethernetHubClient> _hub;
    private readonly ILogger<GroupService> _log;

    public GroupService(
        AethernetDbContext db,
        IHubContext<AethernetHub, IAethernetHubClient> hub,
        ILogger<GroupService> log)
    {
        _db = db; _hub = hub; _log = log;
    }

    public async Task<GroupPasswordDto> CreateAsync(string ownerUid)
    {
        var ownedCount = await _db.Groups.CountAsync(g => g.OwnerUid == ownerUid);
        if (ownedCount >= AethernetConstants.MaxGroupsOwned)
            throw new HubException("group_limit_owned");

        var gid = UidGenerator.NewGid();
        var password = UidGenerator.NewGroupPassword();
        var entity = new GroupEntity
        {
            Gid = gid,
            OwnerUid = ownerUid,
            PasswordHash = SecretKeyHasher.Hash(password),
            CreatedAt = DateTime.UtcNow,
            PasswordRotatedAt = DateTime.UtcNow,
            MemberLimit = AethernetConstants.MaxGroupUsers,
        };
        _db.Groups.Add(entity);
        _db.GroupPairs.Add(new GroupPairEntity
        {
            Gid = gid, Uid = ownerUid, Role = GroupPairUserRole.Owner, JoinedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return new GroupPasswordDto(new GroupDto(gid), password);
    }

    public async Task<GroupFullInfoDto> JoinAsync(string uid, GroupPasswordDto join)
    {
        var group = await _db.Groups.Include(g => g.Members)
                       .FirstOrDefaultAsync(g => g.Gid == join.Group.GID)
                   ?? throw new HubException("group_not_found");

        if (await _db.GroupBans.AnyAsync(b => b.Gid == group.Gid && b.UidBanned == uid))
            throw new HubException("banned_from_group");
        if (group.Permissions.HasFlag(GroupPermissions.DisableInvites))
            throw new HubException("group_invites_disabled");
        if (group.Members.Count >= group.MemberLimit) throw new HubException("group_full");
        if (group.Members.Any(m => m.Uid == uid)) throw new HubException("already_member");

        var joinedCount = await _db.GroupPairs.CountAsync(g => g.Uid == uid);
        if (joinedCount >= AethernetConstants.MaxGroupsJoined) throw new HubException("group_limit_joined");

        if (!SecretKeyHasher.Verify(join.Password, group.PasswordHash))
            throw new HubException("bad_password");

        _db.GroupPairs.Add(new GroupPairEntity
        {
            Gid = group.Gid, Uid = uid, Role = GroupPairUserRole.Member, JoinedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        var newMember = new GroupPairFullInfoDto(new GroupDto(group.Gid), new UserDto(uid),
            GroupUserPreferredPermissions.None, GroupPairUserRole.Member);
        foreach (var existing in group.Members)
            await _hub.Clients.User(existing.Uid).Client_GroupPairJoined(newMember);

        return await BuildFullInfo(group.Gid);
    }

    public async Task LeaveAsync(string uid, GroupDto group)
    {
        var row = await _db.GroupPairs.FirstOrDefaultAsync(g => g.Gid == group.GID && g.Uid == uid);
        if (row is null) return;
        if (row.Role == GroupPairUserRole.Owner)
            throw new HubException("owner_must_transfer_or_delete");
        _db.GroupPairs.Remove(row);
        await _db.SaveChangesAsync();
        await BroadcastPairLeft(group.GID, uid);
    }

    public async Task DeleteAsync(string uid, GroupDto group)
    {
        var grp = await _db.Groups.FirstOrDefaultAsync(g => g.Gid == group.GID)
                  ?? throw new HubException("group_not_found");
        if (grp.OwnerUid != uid) throw new HubException("not_owner");
        var members = await _db.GroupPairs.Where(g => g.Gid == group.GID).Select(g => g.Uid).ToListAsync();
        _db.Groups.Remove(grp);
        await _db.SaveChangesAsync();
        foreach (var m in members)
            await _hub.Clients.User(m).Client_GroupDelete(group);
    }

    public async Task<GroupPasswordDto> RotatePasswordAsync(string uid, GroupDto group)
    {
        var grp = await RequireModeratorOrOwner(uid, group.GID);
        var pw = UidGenerator.NewGroupPassword();
        grp.PasswordHash = SecretKeyHasher.Hash(pw);
        grp.PasswordRotatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new GroupPasswordDto(group, pw);
    }

    public async Task SetPermissionsAsync(string uid, GroupDto group, GroupPermissions perms)
    {
        var grp = await RequireOwner(uid, group.GID);
        grp.Permissions = perms;
        await _db.SaveChangesAsync();
        await BroadcastInfo(grp);
    }

    public async Task ChangeOwnershipAsync(string uid, GroupDto group, UserDto newOwner)
    {
        var grp = await RequireOwner(uid, group.GID);
        var newRow = await _db.GroupPairs.FirstOrDefaultAsync(g => g.Gid == group.GID && g.Uid == newOwner.UID)
                     ?? throw new HubException("not_in_group");
        var oldRow = await _db.GroupPairs.FirstAsync(g => g.Gid == group.GID && g.Uid == uid);
        grp.OwnerUid = newOwner.UID;
        newRow.Role  = GroupPairUserRole.Owner;
        oldRow.Role  = GroupPairUserRole.Moderator;
        await _db.SaveChangesAsync();
        await BroadcastInfo(grp);
    }

    public async Task SetUserPermissionsAsync(string uid, GroupPairUserInfoDto info)
    {
        if (info.User.UID != uid) await RequireModeratorOrOwner(uid, info.Group.GID);
        var row = await _db.GroupPairs.FirstOrDefaultAsync(g => g.Gid == info.Group.GID && g.Uid == info.User.UID)
                  ?? throw new HubException("not_in_group");
        row.UserPermissions = info.UserPermissions;
        await _db.SaveChangesAsync();
        var memberUids = await _db.GroupPairs.Where(g => g.Gid == info.Group.GID).Select(g => g.Uid).ToListAsync();
        foreach (var m in memberUids)
            await _hub.Clients.User(m).Client_GroupPairChangeUserInfo(info);
    }

    public async Task RemoveUserAsync(string uid, GroupDto group, UserDto user)
    {
        await RequireModeratorOrOwner(uid, group.GID);
        if (user.UID == uid) throw new HubException("cannot_remove_self");
        var row = await _db.GroupPairs.FirstOrDefaultAsync(g => g.Gid == group.GID && g.Uid == user.UID);
        if (row is null) return;
        if (row.Role == GroupPairUserRole.Owner) throw new HubException("cannot_remove_owner");
        _db.GroupPairs.Remove(row);
        await _db.SaveChangesAsync();
        await BroadcastPairLeft(group.GID, user.UID);
        await _hub.Clients.User(user.UID).Client_GroupDelete(group);
    }

    public async Task BanUserAsync(string uid, GroupDto group, UserDto user, string? reason)
    {
        await RequireModeratorOrOwner(uid, group.GID);
        if (!await _db.GroupBans.AnyAsync(b => b.Gid == group.GID && b.UidBanned == user.UID))
        {
            _db.GroupBans.Add(new GroupBanEntity
            {
                Gid = group.GID, UidBanned = user.UID, UidBannedBy = uid,
                Reason = reason, BannedAt = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync();
        await RemoveUserAsync(uid, group, user);
    }

    public async Task UnbanUserAsync(string uid, GroupDto group, UserDto user)
    {
        await RequireModeratorOrOwner(uid, group.GID);
        var ban = await _db.GroupBans.FirstOrDefaultAsync(b => b.Gid == group.GID && b.UidBanned == user.UID);
        if (ban is null) return;
        _db.GroupBans.Remove(ban);
        await _db.SaveChangesAsync();
    }

    public async Task<List<UserDto>> GetBansAsync(string uid, GroupDto group)
    {
        await RequireModeratorOrOwner(uid, group.GID);
        return await _db.GroupBans
            .Where(b => b.Gid == group.GID)
            .Select(b => new UserDto(b.UidBanned))
            .ToListAsync();
    }

    public async Task ClearAsync(string uid, GroupDto group)
    {
        var grp = await RequireOwner(uid, group.GID);
        var members = await _db.GroupPairs
            .Where(g => g.Gid == group.GID && g.Uid != grp.OwnerUid)
            .ToListAsync();
        var uids = members.Select(m => m.Uid).ToList();
        _db.GroupPairs.RemoveRange(members);
        await _db.SaveChangesAsync();
        foreach (var u in uids)
            await _hub.Clients.User(u).Client_GroupDelete(group);
    }

    public async Task SetModeratorAsync(string uid, GroupDto group, UserDto user, bool isMod)
    {
        await RequireOwner(uid, group.GID);
        var row = await _db.GroupPairs.FirstOrDefaultAsync(g => g.Gid == group.GID && g.Uid == user.UID)
                  ?? throw new HubException("not_in_group");
        row.Role = isMod ? GroupPairUserRole.Moderator : GroupPairUserRole.Member;
        await _db.SaveChangesAsync();
        var info = new GroupPairUserInfoDto(group, user, row.UserPermissions, row.Role);
        var memberUids = await _db.GroupPairs.Where(g => g.Gid == group.GID).Select(g => g.Uid).ToListAsync();
        foreach (var m in memberUids)
            await _hub.Clients.User(m).Client_GroupPairChangeUserInfo(info);
    }

    public async Task SetAliasAsync(string uid, GroupDto group, string? alias)
    {
        var grp = await RequireOwner(uid, group.GID);
        if (alias is not null)
        {
            alias = alias.Trim();
            if (alias.Length is < 3 or > 32) throw new HubException("alias_invalid");
            if (await _db.Groups.AnyAsync(g => g.Alias == alias && g.Gid != group.GID))
                throw new HubException("alias_in_use");
        }
        grp.Alias = alias;
        await _db.SaveChangesAsync();
        await BroadcastInfo(grp);
    }

    private async Task<GroupEntity> RequireOwner(string uid, string gid)
    {
        var grp = await _db.Groups.FirstOrDefaultAsync(g => g.Gid == gid)
                  ?? throw new HubException("group_not_found");
        if (grp.OwnerUid != uid) throw new HubException("not_owner");
        return grp;
    }

    private async Task<GroupEntity> RequireModeratorOrOwner(string uid, string gid)
    {
        var grp = await _db.Groups.FirstOrDefaultAsync(g => g.Gid == gid)
                  ?? throw new HubException("group_not_found");
        if (grp.OwnerUid == uid) return grp;
        var role = await _db.GroupPairs
            .Where(g => g.Gid == gid && g.Uid == uid)
            .Select(g => (GroupPairUserRole?)g.Role).FirstOrDefaultAsync();
        if (role is null) throw new HubException("not_in_group");
        if (role != GroupPairUserRole.Moderator && role != GroupPairUserRole.Owner)
            throw new HubException("forbidden");
        return grp;
    }

    private async Task BroadcastInfo(GroupEntity grp)
    {
        var memberUids = await _db.GroupPairs.Where(g => g.Gid == grp.Gid).Select(g => g.Uid).ToListAsync();
        var info = new GroupInfoDto(
            new GroupDto(grp.Gid) { Alias = grp.Alias ?? "" },
            new UserDto(grp.OwnerUid),
            grp.Permissions,
            memberUids.Count,
            grp.MemberLimit);
        foreach (var m in memberUids)
            await _hub.Clients.User(m).Client_GroupSendInfo(info);
    }

    private async Task<GroupFullInfoDto> BuildFullInfo(string gid)
    {
        var grp = await _db.Groups.FirstAsync(g => g.Gid == gid);
        var members = await _db.GroupPairs.Where(g => g.Gid == gid).ToListAsync();
        return new GroupFullInfoDto(
            new GroupDto(grp.Gid) { Alias = grp.Alias ?? "" },
            new UserDto(grp.OwnerUid),
            grp.Permissions,
            grp.DefaultUserPermissions,
            members.Select(m => new GroupPairFullInfoDto(
                new GroupDto(grp.Gid),
                new UserDto(m.Uid),
                m.UserPermissions,
                m.Role)).ToList());
    }

    private async Task BroadcastPairLeft(string gid, string leftUid)
    {
        var memberUids = await _db.GroupPairs.Where(g => g.Gid == gid).Select(g => g.Uid).ToListAsync();
        foreach (var m in memberUids)
            await _hub.Clients.User(m).Client_GroupPairLeft(new GroupDto(gid), new UserDto(leftUid));
    }
}
