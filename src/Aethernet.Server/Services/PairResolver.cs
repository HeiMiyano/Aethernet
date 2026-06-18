using Aethernet.API.Dto;
using Aethernet.Data;
using Microsoft.EntityFrameworkCore;

namespace Aethernet.Server.Services;

/// <summary>
/// Resolves the user's effective pair list — both direct one-on-one pairs and indirect group
/// pairs (everyone in a shared group is considered paired). A pair is "active" when:
/// (a) it is bidirectional or shared via at least one group, AND
/// (b) neither side has flagged it as paused (the Paused permission is local-only — the hub
///     still allows pushes; the client honors the flag when applying received data).
/// </summary>
public sealed class PairResolver : IPairResolver
{
    private readonly AethernetDbContext _db;
    public PairResolver(AethernetDbContext db) { _db = db; }

    public async Task<bool> IsActivelyPairedAsync(string ownerUid, string otherUid)
    {
        var directOut = await _db.Pairs.AnyAsync(p => p.OwnerUid == ownerUid && p.OtherUid == otherUid);
        var directIn  = await _db.Pairs.AnyAsync(p => p.OwnerUid == otherUid && p.OtherUid == ownerUid);
        if (directOut && directIn) return true;

        // Shared group?
        return await _db.GroupPairs
            .Where(g => g.Uid == ownerUid)
            .AnyAsync(g => _db.GroupPairs.Any(g2 => g2.Gid == g.Gid && g2.Uid == otherUid));
    }

    public async Task<IReadOnlyList<string>> FilterToActivePairsAsync(string ownerUid, IReadOnlyList<string> candidates)
    {
        var pairs = await GetActivePairsAsync(ownerUid);
        var active = pairs.Select(p => p.OtherUid).ToHashSet(StringComparer.Ordinal);
        return candidates.Where(c => active.Contains(c)).ToList();
    }

    public async Task<IReadOnlyList<ResolvedPair>> GetActivePairsAsync(string ownerUid)
    {
        var allPairs = await GetAllVisiblePairsAsync(ownerUid);
        return allPairs
            .Where(p => p.Status == IndividualPairStatus.Bidirectional || p.SharedGroups.Count > 0)
            .ToList();
    }

    public async Task<IReadOnlyList<ResolvedPair>> GetAllVisiblePairsAsync(string ownerUid)
    {
        // Direct pairs (this user added them).
        var outgoing = await _db.Pairs
            .Where(p => p.OwnerUid == ownerUid)
            .Select(p => new { p.OtherUid, p.OwnPermissions })
            .ToListAsync();

        // Reciprocal pairs (those users added us back).
        var incoming = await _db.Pairs
            .Where(p => p.OtherUid == ownerUid)
            .Select(p => new { p.OwnerUid, p.OwnPermissions })
            .ToListAsync();

        var incomingMap = incoming.ToDictionary(x => x.OwnerUid, x => x.OwnPermissions);

        // Shared group memberships.
        var myGroups = await _db.GroupPairs.Where(g => g.Uid == ownerUid).Select(g => g.Gid).ToListAsync();
        var groupPeers = await _db.GroupPairs
            .Where(g => myGroups.Contains(g.Gid) && g.Uid != ownerUid)
            .Select(g => new { g.Gid, g.Uid })
            .ToListAsync();

        // Get aliases (subquery — could be a join but this is clearer).
        var allUids = outgoing.Select(o => o.OtherUid)
            .Concat(incomingMap.Keys)
            .Concat(groupPeers.Select(g => g.Uid))
            .Distinct().ToList();
        var aliases = await _db.Users
            .Where(u => allUids.Contains(u.Uid))
            .ToDictionaryAsync(u => u.Uid, u => u.Alias);

        var sharedGroupsByPeer = groupPeers
            .GroupBy(g => g.Uid)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Gid).Distinct().ToList());

        var result = new Dictionary<string, ResolvedPair>(StringComparer.Ordinal);
        foreach (var o in outgoing)
        {
            var hasReciprocal = incomingMap.ContainsKey(o.OtherUid);
            var status = hasReciprocal ? IndividualPairStatus.Bidirectional : IndividualPairStatus.OneSided;
            var otherPerms = hasReciprocal ? incomingMap[o.OtherUid] : UserPermissions.None;
            var shared = sharedGroupsByPeer.TryGetValue(o.OtherUid, out var gs) ? gs : new();
            result[o.OtherUid] = new ResolvedPair(o.OtherUid, aliases.GetValueOrDefault(o.OtherUid),
                status, o.OwnPermissions, otherPerms, shared);
        }
        foreach (var peer in sharedGroupsByPeer)
        {
            if (result.ContainsKey(peer.Key)) continue; // already covered by direct pair
            var otherPerms = incomingMap.TryGetValue(peer.Key, out var p) ? p : UserPermissions.None;
            result[peer.Key] = new ResolvedPair(peer.Key, aliases.GetValueOrDefault(peer.Key),
                IndividualPairStatus.None, UserPermissions.None, otherPerms, peer.Value);
        }
        return result.Values.ToList();
    }

    public async Task<UserPairDto> BuildUserPairDtoAsync(string ownerUid, string otherUid)
    {
        var resolved = (await GetAllVisiblePairsAsync(ownerUid)).FirstOrDefault(p => p.OtherUid == otherUid)
                       ?? new ResolvedPair(otherUid, null, IndividualPairStatus.None,
                                           UserPermissions.None, UserPermissions.None, new());
        return new UserPairDto(
            new UserDto(otherUid) { Alias = resolved.OtherAlias ?? "" },
            resolved.Status,
            resolved.OwnPermissions,
            resolved.OtherPermissions)
        {
            SharedGroups = resolved.SharedGroups
        };
    }
}
