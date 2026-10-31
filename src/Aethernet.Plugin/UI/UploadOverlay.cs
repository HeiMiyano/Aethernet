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
/// In-world overlay that draws a single progress bar above the local player while we are
/// uploading mod files to the file server, then a short green "uploaded" state when the batch
/// finishes. Same bar as <see cref="DownloadOverlay"/> but orange, so sending vs receiving is
/// obvious. Only appears when files actually need uploading — data the server already has is
/// deduped and never transfers. Hidden when <see cref="AethernetConfig.ShowDownloadProgressBars"/> is off.
/// </summary>
public sealed class UploadOverlay : IDisposable
{
    private readonly IDalamudPluginInterface _pi;
    private readonly IGameGui _gui;
    private readonly IObjectTable _objectTable;
    private readonly FileTransferService _transfer;
    private readonly AethernetConfig _config;
    private readonly ILogger<UploadOverlay> _log;

    public UploadOverlay(
        IDalamudPluginInterface pi, IGameGui gui, IObjectTable objectTable,
        FileTransferService transfer, AethernetConfig config, ILogger<UploadOverlay> log)
    {
        _pi = pi; _gui = gui; _objectTable = objectTable;
        _transfer = transfer; _config = config; _log = log;
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

        var progress = _transfer.SelfUpload;
        var done = progress is null ? _transfer.SelfUploadDone : null;
        if (progress is null && done is null) return;

        // Object table slot 0 is always the local player when logged in. Cast guards against
        // the brief startup window when the slot exists but isn't yet a player character.
        if (_objectTable[0] is not IPlayerCharacter player) return;

        // A bit higher than the download bar (2.7f vs 2.4f) so the two never overlap.
        var worldPos = player.Position;
        var anchor = new Vector3(worldPos.X, worldPos.Y + 2.7f, worldPos.Z);
        if (!_gui.WorldToScreen(anchor, out var screenPos)) return;

        var drawList = ImGui.GetBackgroundDrawList();
        if (progress is not null)
        {
            TransferBar.Draw(drawList, screenPos, (float)progress.Fraction, FormatLabel(progress), TransferBar.Outgoing);
        }
        else if (done is not null)
        {
            var partial = done.Files < done.FilesExpected;
            var label = partial
                ? $"Uploaded {done.Files}/{done.FilesExpected} files (some failed)"
                : $"Uploaded {TransferBar.FormatBytes(done.Bytes)} ({done.Files} files)";
            TransferBar.Draw(drawList, screenPos, partial ? done.Files / (float)done.FilesExpected : 1f,
                label, partial ? TransferBar.Outgoing : TransferBar.Done, TransferBar.FadeAlpha(done));
        }
    }

    private static string FormatLabel(SelfUploadProgress p)
    {
        // "↑ 12.3 MB / 47.8 MB (3/8 files)" — arrow makes direction unambiguous even for users
        // who don't read the color cue.
        if (p.BytesTotal <= 0) return $"↑ uploading {p.FilesDone}/{p.FilesTotal}…";
        return $"↑ {TransferBar.FormatBytes(p.BytesDone)} / {TransferBar.FormatBytes(p.BytesTotal)} ({p.FilesDone}/{p.FilesTotal})";
    }
}
