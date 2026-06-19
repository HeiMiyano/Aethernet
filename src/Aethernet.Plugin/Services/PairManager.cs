using System.Collections.Concurrent;
using Aethernet.API.Dto;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.Services;

/// <summary>
/// In-memory mirror of the user's pair list, with online/offline state. UI reads from this;
/// hub callbacks mutate it.
/// </summary>
public sealed class PairManager
{
    private readonly ConcurrentDictionary<string, PairEntry> _pairs = new(StringComparer.Ordinal);
    private readonly ILogger<PairManager> _log;

    public PairManager(ILogger<PairManager> log) { _log = log; }

    public event Action? PairsChanged;

    public IReadOnlyCollection<PairEntry> All       => _pairs.Values.ToArray();
    public IEnumerable<PairEntry> Online             => _pairs.Values.Where(p => p.IsOnline);
    public IEnumerable<PairEntry> OnlineAndUnpaused  => Online.Where(p => !p.Pair.OwnPermissions.HasFlag(UserPermissions.Paused));

    public void Replace(IEnumerable<UserPairDto> pairs)
    {
        _pairs.Clear();
        foreach (var p in pairs) _pairs[p.User.UID] = new PairEntry(p);
        Raise();
    }

    public void SetOnline(IEnumerable<OnlineUserIdentDto> online)
    {
        var set = online.ToDictionary(o => o.User.UID, o => o);
        foreach (var entry in _pairs.Values)
        {
            if (set.TryGetValue(entry.Pair.User.UID, out var dto))
            {
                entry.IsOnline = true;
                // Capture the latest ident too — VisibleUserManager uses this to match the pair's
                // character against our object table. Without this, after a Reconnect we know the
                // pair is online but can't match them visually because RemoteIdent is stale/null.
                entry.RemoteIdent = dto.Ident;
            }
            else
            {
                entry.IsOnline = false;
            }
        }
        Raise();
    }

    public void MarkOnline(OnlineUserIdentDto ident)
    {
        if (_pairs.TryGetValue(ident.User.UID, out var e))
        {
            e.IsOnline = true; e.RemoteIdent = ident.Ident;
        }
        Raise();
    }

    public void MarkOffline(UserDto user)
    {
        if (_pairs.TryGetValue(user.UID, out var e)) e.IsOnline = false;
        Raise();
    }

    public void AddOrUpdate(UserPairDto dto)
    {
        _pairs.AddOrUpdate(dto.User.UID, _ => new PairEntry(dto), (_, e) => { e.Pair = dto; return e; });
        Raise();
    }

    public void Remove(string uid) { _pairs.TryRemove(uid, out _); Raise(); }

    public void UpdateOtherPermissions(UserPermissionsDto perm)
    {
        if (_pairs.TryGetValue(perm.User.UID, out var e))
            e.Pair = e.Pair with { OtherPermissions = perm.Permissions };
        Raise();
    }

    public void UpdateProfile(UserProfileDto profile)
    {
        if (_pairs.TryGetValue(profile.User.UID, out var e)) e.LatestProfile = profile;
        Raise();
    }

    public bool TryGet(string uid, out PairEntry e) => _pairs.TryGetValue(uid, out e!);

    private void Raise() { try { PairsChanged?.Invoke(); } catch { /* swallow UI listeners */ } }
}

public sealed class PairEntry
{
    public UserPairDto Pair { get; set; }
    public bool IsOnline { get; set; }
    public string? RemoteIdent { get; set; }
    public UserProfileDto? LatestProfile { get; set; }
    public DateTime? LastAppliedAt { get; set; }

    public PairEntry(UserPairDto pair) { Pair = pair; }
}
