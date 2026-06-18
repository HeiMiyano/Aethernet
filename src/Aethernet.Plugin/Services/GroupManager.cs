using System.Collections.Concurrent;
using Aethernet.API.Dto;

namespace Aethernet.Plugin.Services;

public sealed class GroupManager
{
    private readonly ConcurrentDictionary<string, GroupEntry> _groups = new(StringComparer.Ordinal);

    public event Action? GroupsChanged;
    public IReadOnlyCollection<GroupEntry> All => _groups.Values.ToArray();

    public void UpsertInfo(GroupInfoDto info)
    {
        _groups.AddOrUpdate(info.Group.GID,
            _ => new GroupEntry { Info = info },
            (_, e) => { e.Info = info; return e; });
        Raise();
    }

    public void UpsertFull(GroupFullInfoDto full)
    {
        _groups.AddOrUpdate(full.Group.GID,
            _ => new GroupEntry { Full = full },
            (_, e) => { e.Full = full; return e; });
        Raise();
    }

    public void Remove(GroupDto group) { _groups.TryRemove(group.GID, out _); Raise(); }

    public void MemberJoined(GroupPairFullInfoDto member)
    {
        if (_groups.TryGetValue(member.Group.GID, out var e) && e.Full is not null)
        {
            var members = e.Full.Members.Where(m => m.User.UID != member.User.UID).Append(member).ToList();
            e.Full = e.Full with { Members = members };
        }
        Raise();
    }

    public void MemberLeft(GroupDto group, UserDto user)
    {
        if (_groups.TryGetValue(group.GID, out var e) && e.Full is not null)
            e.Full = e.Full with { Members = e.Full.Members.Where(m => m.User.UID != user.UID).ToList() };
        Raise();
    }

    public void MemberInfo(GroupPairUserInfoDto info)
    {
        if (_groups.TryGetValue(info.Group.GID, out var e) && e.Full is not null)
        {
            var members = e.Full.Members.Select(m =>
                m.User.UID == info.User.UID
                    ? m with { UserPermissions = info.UserPermissions, Role = info.Role }
                    : m).ToList();
            e.Full = e.Full with { Members = members };
        }
        Raise();
    }

    private void Raise() { try { GroupsChanged?.Invoke(); } catch { } }
}

public sealed class GroupEntry
{
    public GroupInfoDto? Info { get; set; }
    public GroupFullInfoDto? Full { get; set; }
}
