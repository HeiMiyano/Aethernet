using System.Numerics;
using Aethernet.Plugin.Services;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.UI;

/// <summary>
/// In-world overlay that draws a single progress bar above the local player while we are
/// uploading mod files to the file server. Mirrors <see cref="DownloadOverlay"/> in shape
/// (same bar geometry, same label format) but uses the aggregated SelfUploadProgress from
/// <see cref="FileTransferService"/> and a distinct color so the user can tell at a glance
/// whether they're sending or receiving.
/// </summary>
public sealed class UploadOverlay : IDisposable
{
    private readonly IDalamudPluginInterface _pi;
    private readonly IGameGui _gui;
    private readonly IObjectTable _objectTable;
    private readonly FileTransferService _transfer;
    private readonly ILogger<UploadOverlay> _log;

    public UploadOverlay(
        IDalamudPluginInterface pi, IGameGui gui, IObjectTable objectTable,
        FileTransferService transfer, ILogger<UploadOverlay> log)
    {
        _pi = pi; _gui = gui; _objectTable = objectTable;
        _transfer = transfer; _log = log;
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
        var progress = _transfer.SelfUpload;
        if (progress is null) return;

        // Object table slot 0 is always the local player when logged in. Cast guards against
        // the brief startup window when the slot exists but isn't yet a player character.
        var player = _objectTable[0] as IPlayerCharacter;
        if (player is null) return;

        // Anchor a bit higher than the download bar (2.7f vs 2.4f) so that if both upload
        // AND a download were ever happening on the same character (unlikely but possible
        // during a heavy session), the two bars wouldn't overlap.
        var worldPos = player.Position;
        var anchor = new Vector3(worldPos.X, worldPos.Y + 2.7f, worldPos.Z);
        if (!_gui.WorldToScreen(anchor, out var screenPos)) return;

        var drawList = ImGui.GetBackgroundDrawList();
        DrawProgressBar(drawList, screenPos, player.Name.TextValue, progress);
    }

    private static void DrawProgressBar(ImDrawListPtr drawList, Vector2 anchor, string label, SelfUploadProgress p)
    {
        const float barWidth = 140f;
        const float barHeight = 6f;
        const float padding = 2f;

        // Same shape as DownloadOverlay but warmer color (orange/yellow) so direction is
        // visually obvious: blue = incoming, orange = outgoing.
        var bg     = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f));
        var border = ImGui.GetColorU32(new Vector4(1f, 0.7f, 0.3f, 0.8f));
        var fill   = ImGui.GetColorU32(new Vector4(1f, 0.8f, 0.3f, 0.95f));
        var textCol = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

        var min = new Vector2(anchor.X - barWidth / 2 - padding, anchor.Y);
        var max = new Vector2(anchor.X + barWidth / 2 + padding, anchor.Y + barHeight + padding * 2);
        drawList.AddRectFilled(min, max, bg, 3f);
        drawList.AddRect(min, max, border, 3f);

        var fraction = (float)p.Fraction;
        if (fraction > 0)
        {
            var fillMin = new Vector2(min.X + padding, min.Y + padding);
            var fillMax = new Vector2(fillMin.X + barWidth * fraction, max.Y - padding);
            drawList.AddRectFilled(fillMin, fillMax, fill, 2f);
        }

        var text = FormatLabel(p);
        var textSize = ImGui.CalcTextSize(text);
        var textPos = new Vector2(anchor.X - textSize.X / 2, anchor.Y - textSize.Y - 1);
        drawList.AddRectFilled(
            new Vector2(textPos.X - 3, textPos.Y - 1),
            new Vector2(textPos.X + textSize.X + 3, textPos.Y + textSize.Y + 1),
            ImGui.GetColorU32(new Vector4(0, 0, 0, 0.55f)), 2f);
        drawList.AddText(textPos, textCol, text);
    }

    private static string FormatLabel(SelfUploadProgress p)
    {
        string fmt(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
            _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB",
        };
        // "↑ uploading 12.3 MB / 47.8 MB (3/8 files)" — arrow makes direction unambiguous
        // even for users who don't read the color cue.
        if (p.BytesTotal <= 0) return $"↑ uploading {p.FilesDone}/{p.FilesTotal}…";
        return $"↑ {fmt(p.BytesDone)} / {fmt(p.BytesTotal)} ({p.FilesDone}/{p.FilesTotal})";
    }
}
