using System.Numerics;
using Aethernet.Plugin.Configuration;
using Dalamud.Plugin;
using Dalamud.Bindings.ImGui;

namespace Aethernet.Plugin.UI;

/// <summary>
/// Pushes the user's accent color into ImGui's style each frame. We only override the few colors
/// that read "Aethernet" — buttons, frames, sliders — so plugins layered above ours still look right.
/// </summary>
public sealed class ThemeApplier : IDisposable
{
    private readonly AethernetConfig _config;
    private readonly IDalamudPluginInterface _pi;

    public ThemeApplier(AethernetConfig config, IDalamudPluginInterface pi)
    {
        _config = config; _pi = pi;
        _pi.UiBuilder.Draw += PushTheme;
    }

    private void PushTheme()
    {
        var c = _config.AccentColor;
        if (c is null || c.Length < 4) return;
        var accent  = new Vector4(c[0], c[1], c[2], c[3]);
        var hover   = new Vector4(c[0] * 1.1f, c[1] * 1.1f, c[2] * 1.1f, c[3]);
        var active  = new Vector4(c[0] * 1.3f, c[1] * 1.3f, c[2] * 1.3f, c[3]);

        var style = ImGui.GetStyle();
        style.Colors[(int)ImGuiCol.Button]         = accent with { W = accent.W * 0.6f };
        style.Colors[(int)ImGuiCol.ButtonHovered]  = hover;
        style.Colors[(int)ImGuiCol.ButtonActive]   = active;
        style.Colors[(int)ImGuiCol.FrameBgHovered] = hover  with { W = hover.W * 0.4f };
        style.Colors[(int)ImGuiCol.SliderGrab]     = accent;
        style.Colors[(int)ImGuiCol.SliderGrabActive] = active;
        style.Colors[(int)ImGuiCol.CheckMark]      = accent;
        style.Colors[(int)ImGuiCol.Header]         = accent with { W = accent.W * 0.45f };
        style.Colors[(int)ImGuiCol.HeaderHovered]  = hover;
        style.Colors[(int)ImGuiCol.HeaderActive]   = active;
        style.Colors[(int)ImGuiCol.Tab]            = accent with { W = accent.W * 0.4f };
        style.Colors[(int)ImGuiCol.TabHovered]     = hover;
        style.Colors[(int)ImGuiCol.TabActive]      = active;

        // UI scaling deliberately NOT applied here — style.ScaleAllSizes() mutates the global
        // ImGui style each frame, which compounds (frame N+1 scales an already-scaled style)
        // and breaks every other plugin's UI. Users should use Dalamud's global "Interface
        // Scale" setting in /xlsettings → Look and Feel instead. See git history for the
        // removed UiScale config field.
    }

    public void Dispose() => _pi.UiBuilder.Draw -= PushTheme;
}
