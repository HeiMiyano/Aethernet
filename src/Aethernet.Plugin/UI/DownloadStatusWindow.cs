using System.Numerics;
using System.Linq;
using Aethernet.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace Aethernet.Plugin.UI;

public sealed class DownloadStatusWindow : Window
{
    private readonly FileTransferService _transfer;

    public DownloadStatusWindow(FileTransferService transfer)
        : base("Aethernet Transfers###AethernetDownloads")
    {
        _transfer = transfer;
        Size = new Vector2(420, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var active = _transfer.ActiveTransfers;
        // Files finish downloading (or, if everything was already cached, never start) before
        // Penumbra/IPC application + redraws run — that phase has no per-file entries of its
        // own, so without this block the window would show "No active transfers" while a pair's
        // mods were still visibly being applied.
        var applying = _transfer.PerUidDownloads.Where(kv => kv.Value.Applying).ToArray();

        if (active.Count == 0 && applying.Length == 0)
        {
            ImGui.TextDisabled("No active transfers.");
            return;
        }

        foreach (var t in active)
        {
            var label = $"{t.Kind} {t.Hash[..Math.Min(8, t.Hash.Length)]}…  " +
                        $"({(t.BytesTransferred / 1024.0):N1} / {(t.TotalBytes / 1024.0):N1} KiB)";
            ImGui.ProgressBar((float)t.Fraction, new Vector2(-1, 0), label);
        }

        foreach (var (uid, _) in applying)
            ImGui.ProgressBar(1f, new Vector2(-1, 0), $"Applying mods for {uid}…");
    }
}
