using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Configuration;
using Aethernet.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Bindings.ImGui;

namespace Aethernet.Plugin.UI;

public sealed class SettingsWindow : Window
{
    private readonly AethernetConfig _config;
    private readonly IDalamudPluginInterface _pi;
    private readonly HubConnectionService _hub;
    private readonly SyncOrchestrator _sync;
    private readonly DiscordSignInService _discord;
    private readonly FirstRunWizard _wizard;
    private DateTime _copyFeedbackUntil = DateTime.MinValue;
    private DateTime _pushFeedbackUntil = DateTime.MinValue;

    public SettingsWindow(AethernetConfig config, IDalamudPluginInterface pi, HubConnectionService hub, SyncOrchestrator sync,
                          DiscordSignInService discord, FirstRunWizard wizard)
        : base("Aethernet Settings###AethernetSettings")
    {
        _config = config; _pi = pi; _hub = hub; _sync = sync; _discord = discord; _wizard = wizard;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.CollapsingHeader("Servers", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // Server endpoints are baked into the plugin at build time so this distribution
            // only ever connects to official Aethernet infrastructure. To run on a different
            // deployment, edit src/Aethernet.Plugin/AethernetServers.cs and rebuild from source.
            ImGui.TextDisabled("Auth:  "); ImGui.SameLine(); ImGui.TextUnformatted(AethernetServers.AuthUrl);
            ImGui.TextDisabled("Hub:   "); ImGui.SameLine(); ImGui.TextUnformatted(AethernetServers.HubUrl);
            ImGui.TextDisabled("Files: "); ImGui.SameLine(); ImGui.TextUnformatted(AethernetServers.FileUrl);
        }

        if (ImGui.CollapsingHeader("Account", ImGuiTreeNodeFlags.DefaultOpen))
        {
            // UID with a Copy button — friends need to swap UIDs to pair, this saves typing.
            ImGui.TextDisabled("UID:");
            ImGui.SameLine();
            if (!string.IsNullOrEmpty(_config.Uid))
            {
                ImGui.Text(_config.Uid);
                ImGui.SameLine();
                if (ImGui.SmallButton($"Copy##uid"))
                {
                    ImGui.SetClipboardText(_config.Uid);
                    _copyFeedbackUntil = DateTime.UtcNow.AddSeconds(2);
                }
                if (DateTime.UtcNow < _copyFeedbackUntil)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.3f, 1f), "copied!");
                }
            }
            else
            {
                ImGui.TextDisabled("<not registered>");
            }

            ImGui.TextDisabled($"Secret key: {(string.IsNullOrEmpty(_config.SecretKey) ? "<none>" : "set (hidden)")}");

            if (string.IsNullOrEmpty(_config.Uid))
            {
                DiscordSignInWidget.Draw(_discord, "Sign in with Discord", _discord.StartSignIn, "settings_signin");
            }
            else if (_hub.DiscordLinkRequired)
            {
                ImGui.TextColored(new Vector4(0.95f, 0.7f, 0.3f, 1f), "This account must be linked to Discord before it can connect.");
                DiscordSignInWidget.Draw(_discord, "Link Discord", _discord.StartLink, "settings_link");
            }
            else if (!_config.DiscordLinked || _discord.IsBusy || _discord.Phase == DiscordSignInService.SignInPhase.Failed)
            {
                ImGui.TextWrapped("Link this account to Discord — it'll soon be required to connect, and it " +
                                  "lets you recover the account if you lose your secret key.");
                DiscordSignInWidget.Draw(_discord, "Link Discord", _discord.StartLink, "settings_link");
            }
            else if (_discord.Phase == DiscordSignInService.SignInPhase.Succeeded)
            {
                ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.3f, 1f), "Discord linked.");
            }
            if (ImGui.Button("Setup wizard")) { _wizard.ShowAccountStep(); _wizard.IsOpen = true; }
            ImGui.SameLine();
            if (ImGui.Button("Reconnect")) { _ = _hub.StopAsync().ContinueWith(_ => _hub.StartAsync()); }
            ImGui.SameLine();
            if (ImGui.Button("Push now"))
            {
                _sync.Schedule(0);
                _pushFeedbackUntil = DateTime.UtcNow.AddSeconds(2);
            }
            if (DateTime.UtcNow < _pushFeedbackUntil)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.3f, 1f), "scheduled!");
            }
        }

        if (ImGui.CollapsingHeader("Behaviour"))
        {
            var auto = _config.AutoConnectOnStartup;
            if (ImGui.Checkbox("Auto-connect on startup", ref auto)) _config.AutoConnectOnStartup = auto;

            var pauseCity = _config.PauseOutsideOfCities;
            if (ImGui.Checkbox("Pause when outside major cities", ref pauseCity)) _config.PauseOutsideOfCities = pauseCity;

            var bars = _config.ShowDownloadProgressBars;
            if (ImGui.Checkbox("Show in-world sync progress bars", ref bars)) _config.ShowDownloadProgressBars = bars;
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Blue bar over paired players while their mods download and apply;\norange bar over you while your files upload.");

            var debounce = _config.DataPushDebounceMs;
            if (ImGui.SliderInt("Data-push debounce (ms)", ref debounce, 50, 2000)) _config.DataPushDebounceMs = debounce;

            var maxDl = _config.MaxParallelDownloads;
            if (ImGui.SliderInt("Max parallel downloads", ref maxDl, 1, 16)) _config.MaxParallelDownloads = maxDl;

            var maxUp = _config.MaxParallelUploads;
            if (ImGui.SliderInt("Max parallel uploads", ref maxUp, 1, 8)) _config.MaxParallelUploads = maxUp;
        }

        if (ImGui.CollapsingHeader("Cache"))
        {
            ImGui.TextDisabled($"Directory: {_config.FileCacheDirectory}");
            var cacheGiB = (int)(_config.MaxCacheSizeBytes / (1024L * 1024 * 1024));
            if (ImGui.SliderInt("Max cache size (GiB)", ref cacheGiB, 1, 200))
                _config.MaxCacheSizeBytes = (long)cacheGiB * 1024 * 1024 * 1024;
        }

        if (ImGui.CollapsingHeader("Theming"))
        {
            // The accent-color picker that lived here was removed — it was overwriting
            // ImGui's shared style colors every frame with nothing to revert them, which bled
            // into Dalamud's own UI and other plugins' windows instead of staying scoped to
            // Aethernet's. There's no color override now; Dalamud's own theme applies as-is.
            //
            // Per-plugin UI scaling was removed earlier for the same reason — ImGui.ScaleAllSizes
            // mutates the global style each frame, which compounds and breaks every other
            // Dalamud plugin's UI. Use Dalamud's global "Interface Scale" in /xlsettings →
            // Look and Feel instead.
            ImGui.TextDisabled("UI scale is set globally — see /xlsettings → Look and Feel.");
        }

        ImGui.Separator();
        if (ImGui.Button("Save"))
        {
            _pi.SavePluginConfig(_config);
            IsOpen = false;
        }
    }
}
