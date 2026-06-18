using System.Numerics;
using Aethernet.Plugin.Services;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.UI;

/// <summary>
/// In-world overlay that draws a small progress bar under each paired character whose
/// data is currently downloading. Subscribes to FileTransferService per-UID progress
/// events; resolves character world position via VisibleUserManager + IGameGui.WorldToScreen.
/// </summary>
public sealed class DownloadOverlay : IDisposable
{
    private readonly IDalamudPluginInterface _pi;
    private readonly IGameGui _gui;
    private readonly IObjectTable _objectTable;
    private readonly FileTransferService _transfer;
    private readonly VisibleUserManager _visible;
    private readonly ILogger<DownloadOverlay> _log;

    public DownloadOverlay(
        IDalamudPluginInterface pi, IGameGui gui, IObjectTable objectTable,
        FileTransferService transfer, VisibleUserManager visible,
        ILogger<DownloadOverlay> log)
    {
        _pi = pi; _gui = gui; _objectTable = objectTable;
        _transfer = transfer; _visible = visible; _log = log;
    }

    public void Initialize()
    {
        _pi.UiBuilder.Draw += DrawOverlay;
    }

    public void Dispose()
    {
        _pi.UiBuilder.Draw -= DrawOverlay;
    }

    private void DrawOverlay()
    {
        var snapshot = _transfer.PerUidDownloads;
        if (snapshot.Count == 0) return;

        // Use a full-screen invisible "background" window so we can draw with ImGui's
        // foreground draw list without interfering with regular plugin windows.
        var fg = ImGui.GetBackgroundDrawList();

        foreach (var (uid, progress) in snapshot)
        {
            var visible = _visible.Get(uid);
            if (visible is null) continue;
            var actor = _objectTable[visible.ObjectIndex] as IPlayerCharacter;
            if (actor is null) continue;

            // Anchor the bar slightly above the character's feet (worldPos + (0, height, 0)
            // is the head; we want the bar above the head). Y = 2.2f is a reasonable height
            // for most player models.
            var worldPos = actor.Position;
            var anchor = new Vector3(worldPos.X, worldPos.Y + 2.4f, worldPos.Z);
            if (!_gui.WorldToScreen(anchor, out var screenPos)) continue;

            DrawProgressBar(fg, screenPos, actor.Name.TextValue, progress);
        }
    }

    private static void DrawProgressBar(ImDrawListPtr drawList, Vector2 anchor, string label, UidDownloadProgress p)
    {
        const float barWidth = 140f;
        const float barHeight = 6f;
        const float padding = 2f;

        // Bar background (semi-transparent black)
        var bg     = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f));
        var border = ImGui.GetColorU32(new Vector4(0.4f, 0.7f, 1f, 0.8f));
        var fill   = ImGui.GetColorU32(new Vector4(0.3f, 0.8f, 1f, 0.95f));
        var textCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

        // Center the bar on the anchor X
        var min = new Vector2(anchor.X - barWidth / 2 - padding, anchor.Y);
        var max = new Vector2(anchor.X + barWidth / 2 + padding, anchor.Y + barHeight + padding * 2);
        drawList.AddRectFilled(min, max, bg, 3f);
        drawList.AddRect(min, max, border, 3f);

        // Filled portion proportional to fraction
        var fraction = (float)p.Fraction;
        if (fraction > 0)
        {
            var fillMin = new Vector2(min.X + padding, min.Y + padding);
            var fillMax = new Vector2(fillMin.X + barWidth * fraction, max.Y - padding);
            drawList.AddRectFilled(fillMin, fillMax, fill, 2f);
        }

        // Label above the bar: "Aethernet sync: 12.3 MB / 47.8 MB (3/8 files)"
        var text = FormatLabel(p);
        var textSize = ImGui.CalcTextSize(text);
        var textPos = new Vector2(anchor.X - textSize.X / 2, anchor.Y - textSize.Y - 1);
        // Backdrop for legibility
        drawList.AddRectFilled(
            new Vector2(textPos.X - 3, textPos.Y - 1),
            new Vector2(textPos.X + textSize.X + 3, textPos.Y + textSize.Y + 1),
            ImGui.GetColorU32(new Vector4(0, 0, 0, 0.55f)), 2f);
        drawList.AddText(textPos, textCol, text);
    }

    private static string FormatLabel(UidDownloadProgress p)
    {
        string fmt(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
            _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB",
        };
        if (p.BytesTotal <= 0) return $"syncing {p.FilesDone}/{p.FilesTotal}…";
        return $"{fmt(p.BytesDone)} / {fmt(p.BytesTotal)} ({p.FilesDone}/{p.FilesTotal})";
    }
}
