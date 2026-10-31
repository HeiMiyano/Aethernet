using System.Numerics;
using Aethernet.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace Aethernet.Plugin.UI;

/// <summary>
/// Shared drawing for the in-world sync bars (<see cref="DownloadOverlay"/> and
/// <see cref="UploadOverlay"/>): same geometry, accent color tells direction —
/// blue = incoming, orange = outgoing, green = just finished.
/// </summary>
internal static class TransferBar
{
    public static readonly Vector4 Incoming = new(0.3f, 0.8f, 1f, 1f);
    public static readonly Vector4 Outgoing = new(1f, 0.8f, 0.3f, 1f);
    public static readonly Vector4 Done     = new(0.35f, 0.85f, 0.45f, 1f);

    private const float FadeSeconds = 0.5f;

    /// <summary>1 while a completed bar is fresh, fading to 0 over its last half-second.</summary>
    public static float FadeAlpha(CompletedTransfer c)
    {
        var remaining = (FileTransferService.CompletionLinger - (DateTime.UtcNow - c.At)).TotalSeconds;
        return (float)Math.Clamp(remaining / FadeSeconds, 0, 1);
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB",
    };

    public static void Draw(ImDrawListPtr drawList, Vector2 anchor, float fraction, string text, Vector4 accent, float alpha = 1f)
    {
        const float barWidth = 140f;
        const float barHeight = 6f;
        const float padding = 2f;
        if (alpha <= 0f) return;

        uint Col(Vector4 c, float a) => ImGui.GetColorU32(new Vector4(c.X, c.Y, c.Z, a * alpha));
        var black = new Vector4(0f, 0f, 0f, 1f);
        var white = new Vector4(1f, 1f, 1f, 1f);

        var min = new Vector2(anchor.X - barWidth / 2 - padding, anchor.Y);
        var max = new Vector2(anchor.X + barWidth / 2 + padding, anchor.Y + barHeight + padding * 2);
        drawList.AddRectFilled(min, max, Col(black, 0.55f), 3f);
        drawList.AddRect(min, max, Col(accent, 0.8f), 3f);

        fraction = Math.Clamp(fraction, 0f, 1f);
        if (fraction > 0)
        {
            var fillMin = new Vector2(min.X + padding, min.Y + padding);
            var fillMax = new Vector2(fillMin.X + barWidth * fraction, max.Y - padding);
            drawList.AddRectFilled(fillMin, fillMax, Col(accent, 0.95f), 2f);
        }

        // Label above the bar, with a backdrop for legibility.
        var textSize = ImGui.CalcTextSize(text);
        var textPos = new Vector2(anchor.X - textSize.X / 2, anchor.Y - textSize.Y - 1);
        drawList.AddRectFilled(
            new Vector2(textPos.X - 3, textPos.Y - 1),
            new Vector2(textPos.X + textSize.X + 3, textPos.Y + textSize.Y + 1),
            Col(black, 0.55f), 2f);
        drawList.AddText(textPos, Col(white, 1f), text);
    }
}
