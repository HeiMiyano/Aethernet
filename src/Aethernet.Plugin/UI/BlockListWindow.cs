using System.Numerics;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace Aethernet.Plugin.UI;

public sealed class BlockListWindow : Window
{
    private readonly HubConnectionService _hub;
    private readonly List<UserDto> _blocked = new();
    private string _newBlockUid = string.Empty;
    private string _newBlockReason = string.Empty;
    private DateTime _lastRefresh = DateTime.MinValue;

    public BlockListWindow(HubConnectionService hub) : base("Aethernet — Block list###AethernetBlockList")
    {
        _hub = hub;
        Size = new Vector2(420, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        Refresh();
    }

    private async void Refresh()
    {
        try
        {
            _lastRefresh = DateTime.UtcNow;
            var rows = await _hub.InvokeAsync<List<UserDto>>(HubMethods.Server.UserGetBlocked);
            _blocked.Clear();
            _blocked.AddRange(rows);
        }
        catch { /* not connected */ }
    }

    public override void Draw()
    {
        ImGui.TextDisabled("Blocked users cannot pair with you, and any existing pair is severed.");
        ImGui.Separator();

        ImGui.InputTextWithHint("##block_uid",    "user UID to block (u-…)", ref _newBlockUid,    32);
        ImGui.InputTextWithHint("##block_reason", "optional reason (private)", ref _newBlockReason, 256);
        if (ImGui.Button("Block") && !string.IsNullOrWhiteSpace(_newBlockUid))
        {
            _ = _hub.InvokeAsync(HubMethods.Server.UserBlock, new UserDto(_newBlockUid.Trim()),
                string.IsNullOrWhiteSpace(_newBlockReason) ? null : _newBlockReason)
                    .ContinueWith(_ => Refresh());
            _newBlockUid = ""; _newBlockReason = "";
        }
        ImGui.SameLine();
        if (ImGui.Button("Refresh")) Refresh();
        ImGui.SameLine();
        ImGui.TextDisabled($"updated {(DateTime.UtcNow - _lastRefresh).TotalSeconds:N0}s ago");

        ImGui.Separator();
        if (!ImGui.BeginChild("##blocked_list", new Vector2(0, -1), true)) { ImGui.EndChild(); return; }
        foreach (var u in _blocked)
        {
            ImGui.TextUnformatted(u.ToString());
            ImGui.SameLine(ImGui.GetWindowWidth() - 70);
            if (ImGui.SmallButton($"Unblock##{u.UID}"))
                _ = _hub.InvokeAsync(HubMethods.Server.UserUnblock, u).ContinueWith(_ => Refresh());
        }
        ImGui.EndChild();
    }
}
