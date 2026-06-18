using System.Numerics;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace Aethernet.Plugin.UI;

/// <summary>
/// Per-group admin window — opened from the group list. Mirrors the moderator/owner-only
/// surface: rotate password, set alias, edit permissions, kick/ban/promote members, view bans,
/// transfer ownership, delete.
/// </summary>
public sealed class GroupAdminWindow : Window
{
    private readonly HubConnectionService _hub;
    private readonly GroupManager _groups;
    private GroupDto? _target;
    private string _aliasBuffer = string.Empty;
    private string _pwReveal = string.Empty;
    private DateTime _pwRevealAt;
    private List<UserDto>? _bans;
    private string _newModUid = string.Empty;
    private string _newOwnerUid = string.Empty;

    public GroupAdminWindow(HubConnectionService hub, GroupManager groups)
        : base("Group admin###AethernetGroupAdmin")
    {
        _hub = hub; _groups = groups;
        Size = new Vector2(520, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Show(GroupDto g)
    {
        _target = g;
        IsOpen = true;
        WindowName = $"Group admin: {g}###AethernetGroupAdmin";
        var entry = _groups.All.FirstOrDefault(e =>
            (e.Full?.Group.GID ?? e.Info?.Group.GID) == g.GID);
        _aliasBuffer = entry?.Full?.Group.Alias ?? entry?.Info?.Group.Alias ?? string.Empty;
        _bans = null;
    }

    public override void Draw()
    {
        if (_target is null) { ImGui.TextDisabled("No group selected."); return; }
        var entry = _groups.All.FirstOrDefault(e =>
            (e.Full?.Group.GID ?? e.Info?.Group.GID) == _target.GID);
        if (entry is null) { ImGui.TextDisabled("Group not present in client cache."); return; }

        var full = entry.Full;
        var info = entry.Info;

        ImGui.TextUnformatted($"GID:    {_target.GID}");
        ImGui.TextUnformatted($"Owner:  {(full?.Owner ?? info?.Owner)?.ToString() ?? "?"}");
        ImGui.TextUnformatted($"Members: {full?.Members.Count ?? info?.MemberCount ?? 0} / {info?.MemberLimit ?? 0}");

        ImGui.Separator();
        if (ImGui.CollapsingHeader("Alias", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.InputText("##alias", ref _aliasBuffer, 32);
            ImGui.SameLine();
            if (ImGui.Button("Set alias"))
                _ = _hub.InvokeAsync(HubMethods.Server.GroupSetAlias, _target,
                    string.IsNullOrWhiteSpace(_aliasBuffer) ? null : _aliasBuffer.Trim());
        }

        if (ImGui.CollapsingHeader("Password"))
        {
            if (ImGui.Button("Rotate password"))
                _ = RotatePasswordAsync();
            if (!string.IsNullOrEmpty(_pwReveal))
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f),
                    $"NEW PASSWORD: {_pwReveal}   (visible for {15 - (int)(DateTime.UtcNow - _pwRevealAt).TotalSeconds}s)");
                if ((DateTime.UtcNow - _pwRevealAt).TotalSeconds > 15) _pwReveal = "";
            }
        }

        if (ImGui.CollapsingHeader("Permissions"))
        {
            var perms = full?.GroupPermissions ?? info?.GroupPermissions ?? GroupPermissions.None;
            var changed = false;
            foreach (GroupPermissions p in Enum.GetValues<GroupPermissions>())
            {
                if (p == GroupPermissions.None) continue;
                var has = perms.HasFlag(p);
                if (ImGui.Checkbox(p.ToString(), ref has))
                {
                    perms = has ? perms | p : perms & ~p;
                    changed = true;
                }
            }
            if (changed)
                _ = _hub.InvokeAsync(HubMethods.Server.GroupSetPermissions, _target, perms);
        }

        if (full is not null && ImGui.CollapsingHeader("Members", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var m in full.Members)
            {
                ImGui.TextUnformatted($"{m.User} — {m.Role}");
                ImGui.SameLine(ImGui.GetWindowWidth() - 220);
                if (ImGui.SmallButton($"Mod##{m.User.UID}"))
                    _ = _hub.InvokeAsync(HubMethods.Server.GroupSetUserModerator, _target, m.User, true);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Demote##{m.User.UID}"))
                    _ = _hub.InvokeAsync(HubMethods.Server.GroupSetUserModerator, _target, m.User, false);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Kick##{m.User.UID}"))
                    _ = _hub.InvokeAsync(HubMethods.Server.GroupRemoveUser, _target, m.User);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Ban##{m.User.UID}"))
                    _ = _hub.InvokeAsync(HubMethods.Server.GroupBanUser, _target, m.User, (string?)null);
            }
        }

        if (ImGui.CollapsingHeader("Bans"))
        {
            if (ImGui.Button("Refresh bans"))
                _ = RefreshBansAsync();
            if (_bans is not null)
            {
                foreach (var u in _bans)
                {
                    ImGui.TextUnformatted(u.ToString());
                    ImGui.SameLine(ImGui.GetWindowWidth() - 70);
                    if (ImGui.SmallButton($"Unban##{u.UID}"))
                        _ = _hub.InvokeAsync(HubMethods.Server.GroupUnbanUser, _target, u).ContinueWith(_ => RefreshBansAsync());
                }
            }
        }

        if (ImGui.CollapsingHeader("Danger zone"))
        {
            ImGui.InputTextWithHint("##new_owner", "uid to transfer ownership to", ref _newOwnerUid, 32);
            ImGui.SameLine();
            if (ImGui.Button("Transfer") && !string.IsNullOrWhiteSpace(_newOwnerUid))
                _ = _hub.InvokeAsync(HubMethods.Server.GroupChangeOwnership, _target, new UserDto(_newOwnerUid.Trim()));

            if (ImGui.Button("Clear all members (keeps owner)"))
                _ = _hub.InvokeAsync(HubMethods.Server.GroupClearAll, _target);
            ImGui.SameLine();
            if (ImGui.Button("DELETE group"))
                _ = _hub.InvokeAsync(HubMethods.Server.GroupDelete, _target);
        }
    }

    private async Task RotatePasswordAsync()
    {
        try
        {
            var res = await _hub.InvokeAsync<GroupPasswordDto>(HubMethods.Server.GroupChangePassword, _target!);
            _pwReveal = res.Password;
            _pwRevealAt = DateTime.UtcNow;
        }
        catch { /* hub will surface its own error */ }
    }

    private async Task RefreshBansAsync()
    {
        try { _bans = await _hub.InvokeAsync<List<UserDto>>(HubMethods.Server.GroupGetBans, _target!); }
        catch { _bans = new(); }
    }
}
