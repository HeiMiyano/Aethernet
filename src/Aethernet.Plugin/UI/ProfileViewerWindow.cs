using System.Numerics;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace Aethernet.Plugin.UI;

/// <summary>
/// Reusable profile viewer — opened by right-click on a pair or by command.
/// Pulls fresh data from the hub on first open and caches it for the session.
/// </summary>
public sealed class ProfileViewerWindow : Window
{
    private readonly HubConnectionService _hub;
    private UserDto? _target;
    private UserProfileDto? _profile;
    private string? _error;

    public ProfileViewerWindow(HubConnectionService hub) : base("Profile###AethernetProfileViewer")
    {
        _hub = hub;
        Size = new Vector2(380, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Show(UserDto user)
    {
        _target = user;
        _profile = null;
        _error = null;
        IsOpen = true;
        WindowName = $"Profile: {user.ToString()}###AethernetProfileViewer";
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_target is null) return;
        try
        {
            _profile = await _hub.InvokeAsync<UserProfileDto>(HubMethods.Server.UserGetProfile, _target);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    public override void Draw()
    {
        if (_target is null) { ImGui.TextDisabled("No user selected."); return; }

        if (_error is not null)
        {
            ImGui.TextColored(new Vector4(0.86f, 0.32f, 0.32f, 1f), _error);
            if (ImGui.Button("Retry")) _ = LoadAsync();
            return;
        }

        if (_profile is null) { ImGui.TextDisabled("Loading…"); return; }

        ImGui.TextUnformatted($"UID:   {_profile.User.UID}");
        if (!string.IsNullOrEmpty(_profile.User.Alias))
            ImGui.TextUnformatted($"Alias: {_profile.User.Alias}");
        if (_profile.IsNsfw)
            ImGui.TextColored(new Vector4(0.95f, 0.45f, 0.45f, 1f), "[NSFW]");
        if (_profile.IsFlagged)
            ImGui.TextColored(new Vector4(0.90f, 0.78f, 0.20f, 1f), "[Flagged by moderators]");

        ImGui.Separator();
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(_profile.Description)
            ? "(No description set.)"
            : _profile.Description);

        ImGui.Separator();
        if (ImGui.Button("Report profile"))
            ImGui.OpenPopup("##report_popup");

        if (ImGui.BeginPopup("##report_popup"))
        {
            ImGui.TextDisabled("Submit a moderation report:");
            ImGui.InputTextMultiline("##reason", ref _reportReason, 1024, new Vector2(280, 80));
            if (ImGui.Button("Send") && !string.IsNullOrWhiteSpace(_reportReason))
            {
                _ = _hub.InvokeAsync(HubMethods.Server.UserReportProfile,
                    new UserProfileReportDto(_target, _reportReason));
                _reportReason = "";
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private string _reportReason = string.Empty;
}
