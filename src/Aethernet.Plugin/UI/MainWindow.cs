using System.Numerics;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Configuration;
using Aethernet.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Microsoft.AspNetCore.SignalR.Client;

namespace Aethernet.Plugin.UI;

/// <summary>
/// Main window: connection status, pair list with context menu + tooltip, group list with admin button.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly HubConnectionService _hub;
    private readonly PairManager _pairs;
    private readonly GroupManager _groups;
    private readonly VisibleUserManager _visible;
    private readonly ProfileViewerWindow _profileViewer;
    private readonly GroupAdminWindow _groupAdmin;
    private readonly AethernetConfig _config;

    private string _addPairUid = string.Empty;
    private string _joinGroupGid = string.Empty;
    private string _joinGroupPw  = string.Empty;
    private GroupPasswordDto? _justCreated;
    private DateTime _copyFeedbackUntil = DateTime.MinValue;

    public MainWindow(
        HubConnectionService hub, PairManager pairs, GroupManager groups, VisibleUserManager visible,
        ProfileViewerWindow profileViewer, GroupAdminWindow groupAdmin, AethernetConfig config)
        : base("Aethernet###Aethernet")
    {
        _hub = hub; _pairs = pairs; _groups = groups; _visible = visible;
        _profileViewer = profileViewer; _groupAdmin = groupAdmin; _config = config;
        Size = new Vector2(420, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        DrawStatusBanner();
        ImGui.Separator();

        if (ImGui.BeginTabBar("AethernetTabs"))
        {
            if (ImGui.BeginTabItem("Pairs"))   { DrawPairsTab();  ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Groups"))  { DrawGroupsTab(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("About"))   { DrawAboutTab();  ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void DrawStatusBanner()
    {
        var state = _hub.State;
        var (label, color) = state switch
        {
            HubConnectionState.Connected     => ("Connected",   new Vector4(0.20f, 0.78f, 0.35f, 1f)),
            HubConnectionState.Connecting    => ("Connecting…", new Vector4(0.90f, 0.78f, 0.20f, 1f)),
            HubConnectionState.Reconnecting  => ("Reconnecting…",new Vector4(0.90f, 0.78f, 0.20f, 1f)),
            _                                => ("Disconnected", new Vector4(0.86f, 0.32f, 0.32f, 1f)),
        };
        ImGui.TextColored(color, $"● {label}");
        ImGui.SameLine();
        ImGui.TextDisabled($"protocol v{AethernetConstants.ProtocolVersion}");

        // Your UID + Copy button — exposed prominently so friends can grab it for pairing.
        if (!string.IsNullOrEmpty(_config.Uid))
        {
            ImGui.TextDisabled("Your UID:");
            ImGui.SameLine();
            ImGui.Text(_config.Uid);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy##main_uid"))
            {
                ImGui.SetClipboardText(_config.Uid);
                _copyFeedbackUntil = DateTime.UtcNow.AddSeconds(2);
            }
            if (DateTime.UtcNow < _copyFeedbackUntil)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.3f, 1f), "copied!");
            }
        }
    }

    private void DrawPairsTab()
    {
        ImGui.InputTextWithHint("##add_pair", "user UID to add (u-…)", ref _addPairUid, 32);
        ImGui.SameLine();
        if (ImGui.Button("Add pair") && !string.IsNullOrWhiteSpace(_addPairUid))
        {
            _ = _hub.InvokeAsync(HubMethods.Server.UserAddPair, new UserDto(_addPairUid.Trim()));
            _addPairUid = string.Empty;
        }

        ImGui.Spacing();
        if (!ImGui.BeginChild("##pair_list", new Vector2(0, -1), true)) { ImGui.EndChild(); return; }

        foreach (var p in _pairs.All.OrderByDescending(p => p.IsOnline).ThenBy(p => p.Pair.User.ToString()))
        {
            var visible = _visible.IsVisible(p.Pair.User.UID);
            var dotColor = visible ? new Vector4(0.20f, 0.78f, 0.35f, 1f)
                         : p.IsOnline ? new Vector4(0.40f, 0.60f, 0.95f, 1f)
                         : new Vector4(0.40f, 0.40f, 0.40f, 1f);
            ImGui.TextColored(dotColor, "●");
            ImGui.SameLine();

            var label = p.Pair.User.ToString();
            if (p.Pair.IndividualPairStatus == IndividualPairStatus.OneSided) label += " (pending)";
            if (p.Pair.OwnPermissions.HasFlag(UserPermissions.Paused))        label += " [paused]";
            ImGui.TextUnformatted(label);

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(p.Pair.User.UID);
                if (!string.IsNullOrEmpty(p.Pair.User.Alias)) ImGui.TextDisabled($"alias: {p.Pair.User.Alias}");
                ImGui.TextDisabled(visible ? "Visible nearby" : p.IsOnline ? "Online" : "Offline");
                if (p.Pair.SharedGroups.Count > 0)
                    ImGui.TextDisabled($"shared groups: {string.Join(", ", p.Pair.SharedGroups)}");
                if (p.LatestProfile?.Description is { Length: > 0 } bio)
                {
                    ImGui.Separator();
                    ImGui.TextWrapped(bio.Length > 200 ? bio[..200] + "…" : bio);
                }
                ImGui.EndTooltip();
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                ImGui.OpenPopup($"ctx##{p.Pair.User.UID}");

            if (ImGui.BeginPopup($"ctx##{p.Pair.User.UID}"))
            {
                if (ImGui.MenuItem("View profile"))    _profileViewer.Show(p.Pair.User);
                if (ImGui.MenuItem("Request data"))
                    _ = _hub.InvokeAsync(HubMethods.Server.UserRequestCharacterData, p.Pair.User);
                ImGui.Separator();
                var paused = p.Pair.OwnPermissions.HasFlag(UserPermissions.Paused);
                if (ImGui.MenuItem(paused ? "Resume" : "Pause"))
                {
                    var next = paused ? p.Pair.OwnPermissions & ~UserPermissions.Paused
                                      : p.Pair.OwnPermissions |  UserPermissions.Paused;
                    _ = _hub.InvokeAsync(HubMethods.Server.UserSetPairPermissions,
                        new UserPermissionsDto(p.Pair.User, next));
                }
                if (ImGui.MenuItem("Remove pair"))
                    _ = _hub.InvokeAsync(HubMethods.Server.UserRemovePair, p.Pair.User);
                if (ImGui.MenuItem("Block (hard block)"))
                    _ = _hub.InvokeAsync(HubMethods.Server.UserBlock, p.Pair.User, (string?)null);
                ImGui.EndPopup();
            }

            ImGui.SameLine(ImGui.GetWindowWidth() - 100);
            if (ImGui.SmallButton($"Perms##{p.Pair.User.UID}"))
                ImGui.OpenPopup($"perms##{p.Pair.User.UID}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"X##{p.Pair.User.UID}"))
                _ = _hub.InvokeAsync(HubMethods.Server.UserRemovePair, p.Pair.User);

            if (ImGui.BeginPopup($"perms##{p.Pair.User.UID}"))
            {
                DrawPermissionsEditor(p.Pair.User.UID, p.Pair.OwnPermissions);
                ImGui.EndPopup();
            }
        }
        ImGui.EndChild();
    }

    private void DrawPermissionsEditor(string uid, UserPermissions current)
    {
        var changed = false;
        foreach (UserPermissions perm in Enum.GetValues<UserPermissions>())
        {
            if (perm == UserPermissions.None) continue;
            var has = current.HasFlag(perm);
            if (ImGui.Checkbox(perm.ToString(), ref has))
            {
                current = has ? current | perm : current & ~perm;
                changed = true;
            }
        }
        if (changed)
            _ = _hub.InvokeAsync(HubMethods.Server.UserSetPairPermissions,
                new UserPermissionsDto(new UserDto(uid), current));
    }

    private void DrawGroupsTab()
    {
        if (ImGui.Button("Create new syncshell"))
        {
            _ = _hub.InvokeAsync<GroupPasswordDto>(HubMethods.Server.GroupCreate)
                .ContinueWith(t => { if (t.IsCompletedSuccessfully) _justCreated = t.Result; });
        }
        if (_justCreated is not null)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.3f, 1f),
                $"created {_justCreated.Group.GID} pw={_justCreated.Password}");
        }

        ImGui.Separator();
        ImGui.TextDisabled("Join a syncshell:");
        ImGui.InputTextWithHint("##join_gid", "syncshell ID (g-…)", ref _joinGroupGid, 32);
        ImGui.InputTextWithHint("##join_pw",  "password",          ref _joinGroupPw,  64);
        if (ImGui.Button("Join") && !string.IsNullOrWhiteSpace(_joinGroupGid) && !string.IsNullOrWhiteSpace(_joinGroupPw))
        {
            _ = _hub.InvokeAsync(HubMethods.Server.GroupJoin,
                new GroupPasswordDto(new GroupDto(_joinGroupGid.Trim()), _joinGroupPw));
            _joinGroupGid = ""; _joinGroupPw = "";
        }

        ImGui.Separator();
        foreach (var g in _groups.All)
        {
            var grpRef = g.Full?.Group ?? g.Info?.Group;
            if (grpRef is null) continue;
            var members = g.Full?.Members.Count ?? g.Info?.MemberCount ?? 0;
            ImGui.TextUnformatted($"{grpRef} — {members} members");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Admin##{grpRef.GID}"))
                _groupAdmin.Show(grpRef);
            ImGui.SameLine();
            if (ImGui.SmallButton($"Leave##{grpRef.GID}"))
                _ = _hub.InvokeAsync(HubMethods.Server.GroupLeave, grpRef);
        }
    }

    private void DrawAboutTab()
    {
        ImGui.TextWrapped("Aethernet synchronizes Penumbra mods, Glamourer settings, Customize+, " +
                          "Honorific, SimpleHeels, Moodles and Pet Names with paired players.");
        ImGui.Separator();
        ImGui.TextDisabled($"Protocol version: {AethernetConstants.ProtocolVersion}");
        ImGui.TextDisabled($"Pairs: {_pairs.All.Count}  •  Online: {_pairs.Online.Count()}  •  Visible: {_visible.AllVisible.Count()}");
    }
}
