using System.Security.Claims;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Data;
using Aethernet.Server.Services;
using Aethernet.Shared.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.Server.Hubs;

[Authorize]
public sealed class AethernetHub : Hub<IAethernetHubClient>
{
    private readonly AethernetDbContext _db;
    private readonly IPresenceTracker _presence;
    private readonly IPairResolver _pairs;
    private readonly ICharacterDataDispatcher _dispatch;
    private readonly IGroupService _groups;
    private readonly IRateLimiter _ratelimit;
    private readonly ILogger<AethernetHub> _log;

    public AethernetHub(
        AethernetDbContext db, IPresenceTracker presence, IPairResolver pairs,
        ICharacterDataDispatcher dispatch, IGroupService groups, IRateLimiter ratelimit,
        ILogger<AethernetHub> log)
    {
        _db = db; _presence = presence; _pairs = pairs; _dispatch = dispatch;
        _groups = groups; _ratelimit = ratelimit; _log = log;
    }

    private string Uid => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? throw new HubException("unauthenticated");

    public override async Task OnConnectedAsync()
    {
        var clientProtoStr = Context.GetHttpContext()?.Request.Query["proto"].ToString();
        if (!int.TryParse(clientProtoStr, out var clientProto) || clientProto != AethernetConstants.ProtocolVersion)
        {
            await Clients.Caller.Client_ReceiveServerMessage(new ServerMessageDto(
                MessageSeverity.Error,
                $"Protocol version mismatch. Server expects {AethernetConstants.ProtocolVersion}, you sent '{clientProtoStr}'."));
            Context.Abort();
            return;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Uid == Uid);
        if (user is null || user.IsBanned)
        {
            await Clients.Caller.Client_ReceiveServerMessage(new ServerMessageDto(
                MessageSeverity.Error,
                user is null ? "Account not found." : "Account is banned."));
            Context.Abort();
            return;
        }

        user.LastSeenAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _presence.MarkOnlineAsync(Uid, Context.ConnectionId);
        AethernetMetrics.HubConnectionsActive.Inc();
        await base.OnConnectedAsync();

        // Use the cached character ident from previous session if available; otherwise
        // we fall back to ConnectionId (which is a useless placeholder for visibility
        // matching). The client should call UserSetIdent immediately after connecting.
        var cachedIdent = await _presence.GetIdentAsync(Uid);
        var pairs = await _pairs.GetActivePairsAsync(Uid);
        var myIdent = new OnlineUserIdentDto(
            new UserDto(Uid) { Alias = user.Alias ?? "" },
            cachedIdent ?? Context.ConnectionId);
        foreach (var p in pairs)
            await Clients.User(p.OtherUid).Client_UserSendOnline(myIdent);
    }

    /// <summary>
    /// Client publishes its in-game character identity (Name@WorldID) so paired clients can
    /// match it against their object table for visibility-based mod application.
    /// </summary>
    public async Task UserSetIdent(string ident)
    {
        if (string.IsNullOrWhiteSpace(ident) || ident.Length > 128) return;
        await _presence.SetIdentAsync(Uid, ident);

        // Re-broadcast online status to all pairs with the now-real ident.
        var user  = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Uid == Uid);
        if (user is null) return;
        var pairs = await _pairs.GetActivePairsAsync(Uid);
        var myIdent = new OnlineUserIdentDto(
            new UserDto(Uid) { Alias = user.Alias ?? "" }, ident);
        foreach (var p in pairs)
            await Clients.User(p.OtherUid).Client_UserSendOnline(myIdent);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _presence.MarkOfflineAsync(Uid, Context.ConnectionId);
        AethernetMetrics.HubConnectionsActive.Dec();
        var pairs = await _pairs.GetActivePairsAsync(Uid);
        var me = new UserDto(Uid);
        foreach (var p in pairs)
            await Clients.User(p.OtherUid).Client_UserSendOffline(me);
        await base.OnDisconnectedAsync(exception);
    }

    public Task<DateTime> Heartbeat() => Task.FromResult(DateTime.UtcNow);
    public Task<bool>     CheckClientHealth() => Task.FromResult(true);

    public async Task<List<OnlineUserIdentDto>> UserGetOnlinePairs()
    {
        var pairs = await _pairs.GetActivePairsAsync(Uid);
        var result = new List<OnlineUserIdentDto>();
        foreach (var p in pairs)
        {
            // Use IsOnlineAsync as the authoritative online check — GetPrimaryConnectionAsync
            // returns the SignalR ConnectionId which is useless as the "Ident" payload (it's
            // not a Name@WorldID so the receiver can't visibility-match against their object
            // table). Fall back to ConnectionId only when no real ident has been published yet.
            if (!await _presence.IsOnlineAsync(p.OtherUid)) continue;
            var realIdent = await _presence.GetIdentAsync(p.OtherUid);
            if (string.IsNullOrEmpty(realIdent))
            {
                var conn = await _presence.GetPrimaryConnectionAsync(p.OtherUid);
                if (conn is null) continue;
                realIdent = conn;
            }
            result.Add(new OnlineUserIdentDto(new UserDto(p.OtherUid) { Alias = p.OtherAlias ?? "" }, realIdent));
        }
        return result;
    }

    public async Task<List<UserPairDto>> UserGetPairedClients()
    {
        var pairs = await _pairs.GetAllVisiblePairsAsync(Uid);
        return pairs.Select(p => new UserPairDto(
            new UserDto(p.OtherUid) { Alias = p.OtherAlias ?? "" },
            p.Status,
            p.OwnPermissions,
            p.OtherPermissions)
        {
            SharedGroups = p.SharedGroups
        }).ToList();
    }

    public async Task UserDelete()
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Uid == Uid);
        if (user is null) return;
        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        await Clients.Caller.Client_ReceiveServerMessage(new ServerMessageDto(MessageSeverity.Information, "Account deleted."));
        Context.Abort();
    }

    public async Task<UserProfileDto> UserGetProfile(UserDto target)
    {
        if (!await _pairs.IsActivelyPairedAsync(Uid, target.UID))
            throw new HubException("not_paired");
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Uid == target.UID)
                ?? throw new HubException("not_found");
        return new UserProfileDto(
            new UserDto(u.Uid) { Alias = u.Alias ?? "" },
            u.ProfileIsFlagged, u.ProfileIsNsfw,
            u.ProfileDescription, u.ProfilePictureBase64);
    }

    public async Task UserSetProfile(UserProfileDto profile)
    {
        if (profile.Description?.Length > 4096) throw new HubException("description_too_long");
        if (profile.Base64ProfilePicture?.Length > 350_000) throw new HubException("picture_too_large");

        var u = await _db.Users.FirstOrDefaultAsync(x => x.Uid == Uid)
                ?? throw new HubException("not_found");
        u.ProfileDescription   = profile.Description;
        u.ProfilePictureBase64 = profile.Base64ProfilePicture;
        u.ProfileIsNsfw        = profile.IsNsfw;
        await _db.SaveChangesAsync();

        var pairs = await _pairs.GetActivePairsAsync(Uid);
        var pushed = new UserProfileDto(
            new UserDto(Uid) { Alias = u.Alias ?? "" },
            u.ProfileIsFlagged, u.ProfileIsNsfw,
            u.ProfileDescription, u.ProfilePictureBase64);
        foreach (var p in pairs)
            await Clients.User(p.OtherUid).Client_UserUpdateProfile(pushed);
    }

    public async Task UserReportProfile(UserProfileReportDto report)
    {
        if (!_ratelimit.TryConsume($"report:{Uid}", maxPerMinute: 5)) throw new HubException("rate_limited");
        _db.ProfileReports.Add(new Data.Entities.ProfileReportEntity
        {
            ReporterUid = Uid,
            ReportedUid = report.ReportedUser.UID,
            Reason      = report.Reason,
            CreatedAt   = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    public async Task UserAddPair(UserDto other)
    {
        if (other.UID == Uid) throw new HubException("cannot_pair_self");
        if (!_ratelimit.TryConsume($"addpair:{Uid}", maxPerMinute: 20)) throw new HubException("rate_limited");
        var exists = await _db.Users.AnyAsync(u => u.Uid == other.UID);
        if (!exists) throw new HubException("not_found");

        var blocked = await _db.Blocks.AnyAsync(b =>
            (b.OwnerUid == Uid     && b.OtherUid == other.UID) ||
            (b.OwnerUid == other.UID && b.OtherUid == Uid));
        if (blocked) throw new HubException("blocked");

        var current = await _db.Pairs.CountAsync(p => p.OwnerUid == Uid);
        if (current >= AethernetConstants.MaxPairs) throw new HubException("pair_limit_reached");

        var already = await _db.Pairs.AnyAsync(p => p.OwnerUid == Uid && p.OtherUid == other.UID);
        if (already) return;

        _db.Pairs.Add(new Data.Entities.PairEntity
        {
            OwnerUid = Uid, OtherUid = other.UID,
            CreatedAt = DateTime.UtcNow, OwnPermissions = UserPermissions.None,
        });
        await _db.SaveChangesAsync();

        var dto = await _pairs.BuildUserPairDtoAsync(Uid, other.UID);
        await Clients.Caller.Client_UserAddPair(dto);

        if (dto.IndividualPairStatus == IndividualPairStatus.Bidirectional)
        {
            var mirrored = await _pairs.BuildUserPairDtoAsync(other.UID, Uid);
            await Clients.User(other.UID).Client_UserAddPair(mirrored);

            // OnConnectedAsync only broadcasts online status for pairs that exist at connect time.
            // When the pair becomes bidirectional after both sides are already connected, neither
            // side learns the other is online — so we have to push it explicitly here.
            var me   = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Uid == Uid);
            var them = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Uid == other.UID);

            var theirConnection = await _presence.GetPrimaryConnectionAsync(other.UID);
            var theirCharIdent  = await _presence.GetIdentAsync(other.UID);
            if (theirConnection is not null && them is not null)
            {
                var theirIdent = new OnlineUserIdentDto(
                    new UserDto(other.UID) { Alias = them.Alias ?? "" },
                    theirCharIdent ?? theirConnection);
                await Clients.Caller.Client_UserSendOnline(theirIdent);
            }

            var myConnection = await _presence.GetPrimaryConnectionAsync(Uid);
            var myCharIdent  = await _presence.GetIdentAsync(Uid);
            if (myConnection is not null && me is not null)
            {
                var myIdent = new OnlineUserIdentDto(
                    new UserDto(Uid) { Alias = me.Alias ?? "" },
                    myCharIdent ?? myConnection);
                await Clients.User(other.UID).Client_UserSendOnline(myIdent);
            }
        }
    }

    public async Task UserRemovePair(UserDto other)
    {
        var row = await _db.Pairs.FirstOrDefaultAsync(p => p.OwnerUid == Uid && p.OtherUid == other.UID);
        if (row is null) return;
        _db.Pairs.Remove(row);
        await _db.SaveChangesAsync();
        await Clients.Caller.Client_UserRemovePair(other.UID);
        await Clients.User(other.UID).Client_UserRemovePair(Uid);
    }

    public async Task UserSetPairPermissions(UserPermissionsDto perms)
    {
        var row = await _db.Pairs.FirstOrDefaultAsync(p => p.OwnerUid == Uid && p.OtherUid == perms.User.UID)
                  ?? throw new HubException("not_paired");
        row.OwnPermissions = perms.Permissions;
        await _db.SaveChangesAsync();
        await Clients.User(perms.User.UID).Client_UserUpdatePairPermissions(
            new UserPermissionsDto(new UserDto(Uid), perms.Permissions));
    }

    public async Task UserBlock(UserDto other, string? reason)
    {
        if (other.UID == Uid) throw new HubException("cannot_block_self");
        var existing = await _db.Blocks.AnyAsync(b => b.OwnerUid == Uid && b.OtherUid == other.UID);
        if (!existing)
        {
            _db.Blocks.Add(new Data.Entities.BlockEntity
            {
                OwnerUid = Uid, OtherUid = other.UID,
                CreatedAt = DateTime.UtcNow, Reason = reason,
            });
        }
        var pairs = await _db.Pairs.Where(p =>
            (p.OwnerUid == Uid     && p.OtherUid == other.UID) ||
            (p.OwnerUid == other.UID && p.OtherUid == Uid)).ToListAsync();
        if (pairs.Count > 0) _db.Pairs.RemoveRange(pairs);
        await _db.SaveChangesAsync();

        await Clients.Caller.Client_UserRemovePair(other.UID);
        await Clients.User(other.UID).Client_UserRemovePair(Uid);
    }

    public async Task UserUnblock(UserDto other)
    {
        var row = await _db.Blocks.FirstOrDefaultAsync(b => b.OwnerUid == Uid && b.OtherUid == other.UID);
        if (row is null) return;
        _db.Blocks.Remove(row);
        await _db.SaveChangesAsync();
    }

    public Task<List<UserDto>> UserGetBlocked()
        => _db.Blocks.Where(b => b.OwnerUid == Uid).Select(b => new UserDto(b.OtherUid)).ToListAsync();

    public async Task UserPushData(UserCharaDataMessageDto msg)
    {
        if (!_ratelimit.TryConsume($"push:{Uid}", maxPerMinute: 30))
            throw new HubException("rate_limited");

        var allowed = await _pairs.FilterToActivePairsAsync(Uid, msg.Recipients.Select(r => r.UID).ToList());
        if (allowed.Count == 0) return;

        await _dispatch.DispatchAsync(Uid, allowed, msg.CharacterData, Clients);
    }

    public async Task UserRequestCharacterData(UserDto from)
    {
        if (!await _pairs.IsActivelyPairedAsync(Uid, from.UID))
            throw new HubException("not_paired");
        await Clients.User(from.UID).Client_ReceiveServerMessage(new ServerMessageDto(
            MessageSeverity.Information,
            $"REQUEST_DATA:{Uid}"));
    }

    public Task<GroupPasswordDto> GroupCreate()
    {
        if (!_ratelimit.TryConsume($"groupcreate:{Uid}", maxPerMinute: 3)) throw new HubException("rate_limited");
        return _groups.CreateAsync(Uid);
    }
    /// <summary>Create a syncshell with a user-supplied password. Pass null or whitespace to
    /// auto-generate (equivalent to <see cref="GroupCreate"/>). Shares the same rate-limit
    /// bucket as <c>GroupCreate</c> so users can't bypass the limit by alternating methods.</summary>
    public Task<GroupPasswordDto> GroupCreateWithPassword(string? password)
    {
        if (!_ratelimit.TryConsume($"groupcreate:{Uid}", maxPerMinute: 3)) throw new HubException("rate_limited");
        return _groups.CreateAsync(Uid, password);
    }
    public Task<GroupFullInfoDto> GroupJoin(GroupPasswordDto join)
    {
        if (!_ratelimit.TryConsume($"groupjoin:{Uid}", maxPerMinute: 10)) throw new HubException("rate_limited");
        return _groups.JoinAsync(Uid, join);
    }
    public Task GroupLeave(GroupDto group) => _groups.LeaveAsync(Uid, group);
    public Task GroupDelete(GroupDto group) => _groups.DeleteAsync(Uid, group);
    public Task<GroupPasswordDto> GroupChangePassword(GroupDto group)
    {
        if (!_ratelimit.TryConsume($"grouprotate:{Uid}:{group.GID}", maxPerMinute: 5)) throw new HubException("rate_limited");
        return _groups.RotatePasswordAsync(Uid, group);
    }
    public Task GroupSetPermissions(GroupDto group, GroupPermissions perms) => _groups.SetPermissionsAsync(Uid, group, perms);
    public Task GroupChangeOwnership(GroupDto group, UserDto newOwner) => _groups.ChangeOwnershipAsync(Uid, group, newOwner);
    public Task GroupChangeUserInfo(GroupPairUserInfoDto info) => _groups.SetUserPermissionsAsync(Uid, info);
    public Task GroupRemoveUser(GroupDto group, UserDto user) => _groups.RemoveUserAsync(Uid, group, user);
    public Task GroupBanUser(GroupDto group, UserDto user, string? reason) => _groups.BanUserAsync(Uid, group, user, reason);
    public Task GroupUnbanUser(GroupDto group, UserDto user) => _groups.UnbanUserAsync(Uid, group, user);
    public Task<List<UserDto>> GroupGetBans(GroupDto group) => _groups.GetBansAsync(Uid, group);
    public Task GroupClearAll(GroupDto group) => _groups.ClearAsync(Uid, group);
    public Task GroupSetUserModerator(GroupDto group, UserDto user, bool isMod) => _groups.SetModeratorAsync(Uid, group, user, isMod);
    public Task GroupSetAlias(GroupDto group, string? alias) => _groups.SetAliasAsync(Uid, group, alias);

    private async Task RequireModeratorAsync()
    {
        var u = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Uid == Uid)
                ?? throw new HubException("not_found");
        if (!u.IsAdmin && !u.IsModerator) throw new HubException("forbidden");
    }

    public async Task ModerationBanUser(UserDto user, string? reason)
    {
        await RequireModeratorAsync();
        var existing = await _db.BannedUsers.FirstOrDefaultAsync(b => b.Uid == user.UID);
        if (existing is null)
        {
            _db.BannedUsers.Add(new Data.Entities.BannedUserEntity
            {
                Uid = user.UID, Reason = reason, BannedAt = DateTime.UtcNow,
            });
        }
        var target = await _db.Users.FirstOrDefaultAsync(x => x.Uid == user.UID);
        if (target is not null) { target.IsBanned = true; target.BanReason = reason; }

        _db.AuditLog.Add(new Data.Entities.AuditLogEntity
        {
            ActorUid = Uid, Action = "user.ban", TargetUid = user.UID, Detail = reason, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        await Clients.User(user.UID).Client_ReceiveServerMessage(
            new ServerMessageDto(MessageSeverity.Error, $"You have been banned: {reason ?? "(no reason given)"}"));
    }

    public async Task ModerationUnbanUser(UserDto user)
    {
        await RequireModeratorAsync();
        var ban = await _db.BannedUsers.FirstOrDefaultAsync(b => b.Uid == user.UID);
        if (ban is not null) _db.BannedUsers.Remove(ban);
        var target = await _db.Users.FirstOrDefaultAsync(x => x.Uid == user.UID);
        if (target is not null) { target.IsBanned = false; target.BanReason = null; }
        _db.AuditLog.Add(new Data.Entities.AuditLogEntity
        {
            ActorUid = Uid, Action = "user.unban", TargetUid = user.UID, CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<UserProfileReportDto>> ModerationGetReports()
    {
        await RequireModeratorAsync();
        return await _db.ProfileReports
            .Where(r => !r.Resolved)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new UserProfileReportDto(new UserDto(r.ReportedUid), r.Reason))
            .Take(200)
            .ToListAsync();
    }
}
