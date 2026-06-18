using System.Numerics;
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
        if (active.Count == 0)
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
    }
}
