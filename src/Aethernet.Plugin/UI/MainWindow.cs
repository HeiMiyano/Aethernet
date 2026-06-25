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
    private readonly Dalamud.Plugin.IDalamudPluginInterface _pi;

    private string _addPairUid = string.Empty;
    private string _joinGroupGid = string.Empty;
    private string _joinGroupPw  = string.Empty;
    private GroupPasswordDto? _justCreated;
    private DateTime _copyFeedbackUntil = DateTime.MinValue;

    // Custom-password buffer for the "Create new syncshell" flow. Blank ⇒ server auto-generates.
    private string _createGroupPassword = string.Empty;
    // Most recent server error (e.g. "password_too_short") shown next to the Create button.
    private string? _createGroupError;
    // Which copy button most recently fired, so the inline "Copied!" toast renders next to the
    // right button. Cleared on timeout (see _copyFeedbackUntil).
    private string? _lastCopyKey;

    // Nickname-edit modal state. _nicknameEditUid is non-null while the modal is open;
    // the buffer holds the in-progress text. Both are cleared when the modal is dismissed.
    private string? _nicknameEditUid;
    private string  _nicknameBuffer = string.Empty;
    private const int NicknameMaxLen = 32;

    public MainWindow(
        HubConnectionService hub, PairManager pairs, GroupManager groups, VisibleUserManager visible,
        ProfileViewerWindow profileViewer, GroupAdminWindow groupAdmin, AethernetConfig config,
        Dalamud.Plugin.IDalamudPluginInterface pi)
        : base("Aethernet###Aethernet")
    {
        _hub = hub; _pairs = pairs; _groups = groups; _visible = visible;
        _profileViewer = profileViewer; _groupAdmin = groupAdmin; _config = config; _pi = pi;
        Size = new Vector2(420, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>Resolves the user-visible display name for a pair, preferring (in order):
    /// local nickname → server-side alias → UID. Used both as the pair-list label and for
    /// alphabetical ordering so nicknamed pairs sort by nickname, not by UID.</summary>
    private string DisplayNameFor(PairEntry p)
    {
        if (_config.PairNicknames.TryGetValue(p.Pair.User.UID, out var nick) && !string.IsNullOrWhiteSpace(nick))
            return nick;
        if (!string.IsNullOrWhiteSpace(p.Pair.User.Alias))
            return p.Pair.User.Alias;
        return p.Pair.User.UID;
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

        DrawNicknameEditor();
    }

    /// <summary>Modal popup shown when the user picks "Set nickname…" from a pair's context menu.
    /// Opens once when _nicknameEditUid transitions from null → non-null; closes on Save or Cancel,
    /// at which point _nicknameEditUid is cleared back to null.</summary>
    private void DrawNicknameEditor()
    {
        if (_nicknameEditUid is null) return;

        // OpenPopup is idempotent — calling it every frame while the popup is "open" is fine.
        // We use it to trigger the modal once when _nicknameEditUid first becomes non-null.
        ImGui.OpenPopup("Set nickname##NicknameModal");

        var open = true;
        if (ImGui.BeginPopupModal("Set nickname##NicknameModal", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped($"Local nickname for {_nicknameEditUid} — only visible to you.");
            ImGui.Spacing();
            ImGui.InputText("##nicknameInput", ref _nicknameBuffer, NicknameMaxLen);
            ImGui.Spacing();

            if (ImGui.Button("Save"))
            {
                var trimmed = _nicknameBuffer.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    _config.PairNicknames.Remove(_nicknameEditUid);
                else
                    _config.PairNicknames[_nicknameEditUid] = trimmed;
                _pi.SavePluginConfig(_config);
                _nicknameEditUid = null;
                _nicknameBuffer  = string.Empty;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                _nicknameEditUid = null;
                _nicknameBuffer  = string.Empty;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        // The user dismissed via the close (X) button — reset our state.
        if (!open)
        {
            _nicknameEditUid = null;
            _nicknameBuffer  = string.Empty;
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

        foreach (var p in _pairs.All.OrderByDescending(p => p.IsOnline).ThenBy(p => DisplayNameFor(p)))
        {
            var visible = _visible.IsVisible(p.Pair.User.UID);
            var dotColor = visible ? new Vector4(0.20f, 0.78f, 0.35f, 1f)
                         : p.IsOnline ? new Vector4(0.40f, 0.60f, 0.95f, 1f)
                         : new Vector4(0.40f, 0.40f, 0.40f, 1f);
            ImGui.TextColored(dotColor, "●");
            ImGui.SameLine();

            var label = DisplayNameFor(p);
            if (p.Pair.IndividualPairStatus == IndividualPairStatus.OneSided) label += " (pending)";
            if (p.Pair.OwnPermissions.HasFlag(UserPermissions.Paused))        label += " [paused]";
            ImGui.TextUnformatted(label);

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                // Tooltip always shows the real UID + alias so users can copy/share them.
                // The pair-list label may show a local nickname (resolved by DisplayNameFor),
                // so the tooltip is where to look for the canonical identifiers.
                ImGui.TextUnformatted(p.Pair.User.UID);
                if (!string.IsNullOrEmpty(p.Pair.User.Alias)) ImGui.TextDisabled($"alias: {p.Pair.User.Alias}");
                if (_config.PairNicknames.TryGetValue(p.Pair.User.UID, out var nick) && !string.IsNullOrEmpty(nick))
                    ImGui.TextDisabled($"nickname: {nick}");
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

                // Set / clear local nickname. Seeds the modal buffer with the current nickname
                // so the user can edit it; an empty buffer on Save removes the nickname entirely.
                var hasNick = _config.PairNicknames.TryGetValue(p.Pair.User.UID, out var existingNick);
                if (ImGui.MenuItem(hasNick ? "Edit nickname…" : "Set nickname…"))
                {
                    _nicknameEditUid = p.Pair.User.UID;
                    _nicknameBuffer  = existingNick ?? string.Empty;
                    ImGui.CloseCurrentPopup();
                }
                if (hasNick && ImGui.MenuItem("Clear nickname"))
                {
                    _config.PairNicknames.Remove(p.Pair.User.UID);
                    _pi.SavePluginConfig(_config);
                }

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

    // ----------------------------------------------------------------------------
    //  Per-pair sync permissions editor
    // ----------------------------------------------------------------------------
    // Direction: these permissions apply on RECEIVE. Toggling a "Block their X"
    // checkbox means "this pair's character data still gets pushed to me, but my
    // applier strips X before handing it to Penumbra/Glamourer/etc.". The pair
    // sees nothing different in their own UI; the change only affects what we
    // accept locally.
    // ----------------------------------------------------------------------------

    private sealed record PermRow(UserPermissions Flag, string Label, string Tooltip);

    private static readonly (string Section, PermRow[] Rows)[] PermSections =
    {
        ("Session", new[]
        {
            new PermRow(UserPermissions.Paused,            "Pause sync with this pair",
                "Don't send or apply ANY data with this pair. Effectively a temporary mute."),
        }),
        ("Visuals", new[]
        {
            new PermRow(UserPermissions.DisableAnimations, "Block animations",
                "Don't apply this pair's .pap animation files (idle, emote, walk cycles, etc.)."),
            new PermRow(UserPermissions.DisableVfx,        "Block VFX",
                "Don't apply this pair's .avfx visual-effect files (auras, particle trails)."),
        }),
        ("Audio", new[]
        {
            new PermRow(UserPermissions.DisableSounds,     "Block custom sounds",
                "Don't apply this pair's .scd audio files (replaced footstep/voice/emote sounds)."),
        }),
        ("Companion plugins", new[]
        {
            new PermRow(UserPermissions.DisableCustomize,  "Block Customize+ profiles",
                "Don't apply this pair's Customize+ body-scaling profile."),
            new PermRow(UserPermissions.DisableHonorific,  "Block Honorific titles",
                "Don't show this pair's custom Honorific title nameplate."),
            new PermRow(UserPermissions.DisableMoodles,    "Block Moodles statuses",
                "Don't apply this pair's custom Moodles status icons."),
            new PermRow(UserPermissions.DisableHeels,      "Block SimpleHeels offsets",
                "Don't apply this pair's heel-height vertical offset."),
            new PermRow(UserPermissions.DisablePetNames,   "Block Pet Names",
                "Don't show this pair's custom Pet Names labels."),
        }),
    };

    private static readonly (string Label, string Tooltip, UserPermissions Flags)[] PermPresets =
    {
        ("Full sync",      "Apply everything this pair sends.",
            UserPermissions.None),
        ("Visual only",    "Apply visuals + animations, block social/audio plugins.",
            UserPermissions.DisableSounds | UserPermissions.DisableHonorific |
            UserPermissions.DisableMoodles | UserPermissions.DisableHeels |
            UserPermissions.DisablePetNames),
        ("Minimal",        "Body/gear only — no animations, no VFX, no sounds, no companion-plugin data.",
            UserPermissions.DisableAnimations | UserPermissions.DisableVfx |
            UserPermissions.DisableSounds | UserPermissions.DisableHonorific |
            UserPermissions.DisableMoodles | UserPermissions.DisableHeels |
            UserPermissions.DisablePetNames | UserPermissions.DisableCustomize),
        ("Paused",         "Stop all data exchange with this pair.",
            UserPermissions.Paused),
    };

    // Confirmation state for "Apply to all pairs"
    private string?           _confirmApplyAllSourceUid;
    private UserPermissions   _confirmApplyAllPerms;

    private void DrawPermissionsEditor(string uid, UserPermissions current)
    {
        var changed = false;

        // ---- preset bar ----
        ImGui.TextDisabled("Presets:");
        ImGui.SameLine();
        foreach (var (label, tip, flags) in PermPresets)
        {
            if (ImGui.SmallButton($"{label}##preset_{uid}_{label}"))
            {
                current = flags;
                changed = true;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(tip);
            ImGui.SameLine();
        }
        ImGui.NewLine();
        ImGui.Separator();

        // ---- categorized toggles ----
        foreach (var (section, rows) in PermSections)
        {
            ImGui.TextDisabled(section);
            foreach (var row in rows)
            {
                var has = current.HasFlag(row.Flag);
                if (ImGui.Checkbox($"{row.Label}##{uid}_{row.Flag}", ref has))
                {
                    current = has ? current | row.Flag : current & ~row.Flag;
                    changed = true;
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(360);
                    ImGui.TextUnformatted(row.Tooltip);
                    ImGui.PopTextWrapPos();
                    ImGui.EndTooltip();
                }
            }
            ImGui.Spacing();
        }

        ImGui.Separator();

        // ---- bulk-apply: copy these permissions to ALL pairs ----
        if (ImGui.Button($"Apply to all pairs##applyall_{uid}"))
        {
            _confirmApplyAllSourceUid = uid;
            _confirmApplyAllPerms     = current;
            ImGui.OpenPopup($"confirmApplyAll##{uid}");
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Copy these permission settings to every paired user.");

        if (ImGui.BeginPopupModal($"confirmApplyAll##{uid}", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextWrapped(
                $"Apply these permissions to ALL {_pairs.All.Count} of your pairs?\n" +
                "This will overwrite each pair's current settings.");
            ImGui.Spacing();
            if (ImGui.Button("Confirm##applyallok"))
            {
                ApplyPermissionsToAllPairs(_confirmApplyAllPerms);
                _confirmApplyAllSourceUid = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##applyallno"))
            {
                _confirmApplyAllSourceUid = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (changed)
            _ = _hub.InvokeAsync(HubMethods.Server.UserSetPairPermissions,
                new UserPermissionsDto(new UserDto(uid), current));
    }

    private void ApplyPermissionsToAllPairs(UserPermissions perms)
    {
        foreach (var pair in _pairs.All)
        {
            // Skip pairs that already match — avoids redundant hub calls and rate-limit pressure.
            if (pair.Pair.OwnPermissions == perms) continue;
            _ = _hub.InvokeAsync(HubMethods.Server.UserSetPairPermissions,
                new UserPermissionsDto(pair.Pair.User, perms));
        }
    }

    private void DrawGroupsTab()
    {
        // Optional custom password — blank means the server auto-generates one (legacy behavior).
        // Width clamp keeps the input from blowing out the rest of the row in a narrow window.
        ImGui.PushItemWidth(180f);
        ImGui.InputTextWithHint("##create_group_pw", "password (blank = auto)", ref _createGroupPassword, 128);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        if (ImGui.Button("Create new syncshell"))
        {
            _createGroupError = null;
            var pw = _createGroupPassword.Trim();
            // Always call GroupCreateWithPassword so the server is the single source of truth
            // for validation; passing null for blank input keeps the auto-gen path intact.
            _ = _hub.InvokeAsync<GroupPasswordDto>(HubMethods.Server.GroupCreateWithPassword, string.IsNullOrEmpty(pw) ? null : pw)
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        _justCreated = t.Result;
                        _createGroupPassword = string.Empty;
                    }
                    else
                    {
                        // HubException message bubbles through t.Exception.InnerException.Message;
                        // we surface only the short reason code (e.g. "password_too_short") to
                        // avoid leaking server stack frames into the UI.
                        var msg = t.Exception?.InnerException?.Message ?? "create failed";
                        _createGroupError = msg.Length > 64 ? msg[..64] : msg;
                    }
                });
        }
        if (_createGroupError is not null)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.9f, 0.4f, 0.4f, 1f), _createGroupError);
        }
        if (_justCreated is not null)
        {
            ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.3f, 1f), "Created syncshell — share these with your friends:");

            ImGui.AlignTextToFramePadding();
            ImGui.Text("ID:");
            ImGui.SameLine();
            ImGui.TextUnformatted(_justCreated.Group.GID);
            ImGui.SameLine();
            if (ImGui.Button("Copy##copy_gid"))
            {
                ImGui.SetClipboardText(_justCreated.Group.GID);
                _copyFeedbackUntil = DateTime.UtcNow.AddSeconds(2);
                _lastCopyKey = "gid";
            }
            if (_lastCopyKey == "gid" && DateTime.UtcNow < _copyFeedbackUntil)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.3f, 1f), "copied!");
            }

            ImGui.AlignTextToFramePadding();
            ImGui.Text("Password:");
            ImGui.SameLine();
            ImGui.TextUnformatted(_justCreated.Password);
            ImGui.SameLine();
            if (ImGui.Button("Copy##copy_pw"))
            {
                ImGui.SetClipboardText(_justCreated.Password);
                _copyFeedbackUntil = DateTime.UtcNow.AddSeconds(2);
                _lastCopyKey = "pw";
            }
            if (_lastCopyKey == "pw" && DateTime.UtcNow < _copyFeedbackUntil)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.3f, 1f), "copied!");
            }

            // "Copy both" emits the share text on the same line so it lands cleanly into Discord.
            if (ImGui.Button("Copy both##copy_both"))
            {
                ImGui.SetClipboardText($"Aethernet syncshell — ID: {_justCreated.Group.GID}  Password: {_justCreated.Password}");
                _copyFeedbackUntil = DateTime.UtcNow.AddSeconds(2);
                _lastCopyKey = "both";
            }
            if (_lastCopyKey == "both" && DateTime.UtcNow < _copyFeedbackUntil)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.3f, 0.85f, 0.3f, 1f), "copied!");
            }
            ImGui.SameLine();
            if (ImGui.Button("Dismiss##dismiss_created"))
            {
                _justCreated = null;
                _lastCopyKey = null;
            }
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
