using System.Numerics;
using Aethernet.Plugin.Services;
using Dalamud.Bindings.ImGui;

namespace Aethernet.Plugin.UI;

/// <summary>
/// The "Sign in with Discord" / "Link Discord" button plus its live status line, shared by the
/// setup wizard, Settings, and the main window so all three behave identically. Stateless —
/// everything lives in <see cref="DiscordSignInService"/>.
/// </summary>
internal static class DiscordSignInWidget
{
    private static readonly Vector4 Pending = new(0.90f, 0.78f, 0.20f, 1f);
    private static readonly Vector4 Good    = new(0.20f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Bad     = new(0.86f, 0.32f, 0.32f, 1f);

    /// <param name="id">Unique per call site — ImGui IDs collide if two windows draw this at once.</param>
    public static void Draw(DiscordSignInService svc, string label, Action start, string id)
    {
        var busy = svc.IsBusy;
        if (busy) ImGui.BeginDisabled();
        if (ImGui.Button($"{label}##discord_{id}")) start();
        if (busy) ImGui.EndDisabled();

        if (busy)
        {
            ImGui.SameLine();
            if (ImGui.Button($"Cancel##discord_cancel_{id}")) svc.Cancel();
        }

        if (svc.Phase != DiscordSignInService.SignInPhase.Idle && !string.IsNullOrEmpty(svc.StatusMessage))
        {
            var color = svc.Phase switch
            {
                DiscordSignInService.SignInPhase.Succeeded => Good,
                DiscordSignInService.SignInPhase.Failed    => Bad,
                _                                          => Pending,
            };
            ImGui.TextColored(color, "●");
            ImGui.SameLine();
            ImGui.TextWrapped(svc.StatusMessage);
        }

        if (svc.Phase == DiscordSignInService.SignInPhase.WaitingForBrowser && svc.AuthorizeUrl is { } url)
        {
            if (ImGui.SmallButton($"Browser didn't open? Copy link##discord_copy_{id}"))
                ImGui.SetClipboardText(url);
        }
    }
}
