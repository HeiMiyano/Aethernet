using System.Numerics;
using Aethernet.Plugin.Configuration;
using Aethernet.Plugin.Services;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.UI;

/// <summary>
/// In-world overlay that draws a small progress bar above each paired character whose data is
/// downloading or being applied, then a short green "synced" state once it lands (so a sync
/// served entirely from cache — done in milliseconds — still gives visible feedback).
/// Hidden when <see cref="AethernetConfig.ShowDownloadProgressBars"/> is off.
/// </summary>
public sealed class DownloadOverlay : IDisposable
{
    private readonly IDalamudPluginInterface _pi;
    private readonly IGameGui _gui;
    private readonly IObjectTable _objectTable;
    private readonly FileTransferService _transfer;
    private readonly VisibleUserManager _visible;
    private readonly AethernetConfig _config;
    private readonly ILogger<DownloadOverlay> _log;

    public DownloadOverlay(
        IDalamudPluginInterface pi, IGameGui gui, IObjectTable objectTable,
        FileTransferService transfer, VisibleUserManager visible, AethernetConfig config,
        ILogger<DownloadOverlay> log)
    {
        _pi = pi; _gui = gui; _objectTable = objectTable;
        _transfer = transfer; _visible = visible; _config = config; _log = log;
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
        if (!_config.ShowDownloadProgressBars) return;

        var active = _transfer.PerUidDownloads;
        var recent = _transfer.RecentDownloads;
        if (active.Count == 0 && recent.Count == 0) return;

        var drawList = ImGui.GetBackgroundDrawList();

        foreach (var (uid, progress) in active)
        {
            if (!TryGetAnchor(uid, out var screenPos)) continue;
            // While applying (post-download Penumbra/IPC phase, no finer-grained progress of its
            // own) show a full bar rather than snapping back to empty.
            var fraction = progress.Applying ? 1f : (float)progress.Fraction;
            TransferBar.Draw(drawList, screenPos, fraction, FormatLabel(progress), TransferBar.Incoming);
        }

        foreach (var (uid, done) in recent)
        {
            if (active.ContainsKey(uid)) continue; // a newer sync already started
            if (!TryGetAnchor(uid, out var screenPos)) continue;
            var label = done.Files > 0 ? $"Synced ({done.Files} new files)" : "Synced";
            TransferBar.Draw(drawList, screenPos, 1f, label, TransferBar.Done, TransferBar.FadeAlpha(done));
        }
    }

    private bool TryGetAnchor(string uid, out Vector2 screenPos)
    {
        screenPos = default;
        var visible = _visible.Get(uid);
        if (visible is null) return false;
        if (_objectTable[visible.ObjectIndex] is not IPlayerCharacter actor) return false;

        // Above the head; 2.4f is a reasonable height for most player models.
        var worldPos = actor.Position;
        return _gui.WorldToScreen(new Vector3(worldPos.X, worldPos.Y + 2.4f, worldPos.Z), out screenPos);
    }

    private static string FormatLabel(UidDownloadProgress p)
    {
        if (p.Applying) return "Applying mods…";
        if (p.BytesTotal <= 0) return $"syncing {p.FilesDone}/{p.FilesTotal}…";
        return $"{TransferBar.FormatBytes(p.BytesDone)} / {TransferBar.FormatBytes(p.BytesTotal)} ({p.FilesDone}/{p.FilesTotal})";
    }
}
