using System.Numerics;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Configuration;
using Aethernet.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace Aethernet.Plugin.UI;

public sealed class EditProfileWindow : Window
{
    private readonly AethernetConfig _config;
    private readonly HubConnectionService _hub;
    private string _bioBuffer;
    private bool _nsfw;

    public EditProfileWindow(AethernetConfig config, HubConnectionService hub)
        : base("Edit Profile###AethernetEditProfile")
    {
        _config = config; _hub = hub;
        _bioBuffer = config.ProfileBio ?? string.Empty;
        _nsfw      = config.ProfileIsNsfw;
        Size = new Vector2(420, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        ImGui.TextDisabled("Bio (markdown allowed, max 4096 chars)");
        ImGui.InputTextMultiline("##bio", ref _bioBuffer, 4096, new Vector2(-1, 180));
        ImGui.Checkbox("This profile contains adult content (NSFW)", ref _nsfw);

        ImGui.Separator();
        if (ImGui.Button("Save"))
        {
            _config.ProfileBio    = _bioBuffer;
            _config.ProfileIsNsfw = _nsfw;
            _ = _hub.InvokeAsync(HubMethods.Server.UserSetProfile,
                new UserProfileDto(new UserDto(_config.Uid ?? ""), false, _nsfw, _bioBuffer, _config.ProfilePictureBase64));
            IsOpen = false;
        }
    }
}
