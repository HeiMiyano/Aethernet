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
    private string _registerRecoverySecret = string.Empty;
    private DateTime _copyFeedbackUntil = DateTime.MinValue;
    private DateTime _pushFeedbackUntil = DateTime.MinValue;

    public SettingsWindow(AethernetConfig config, IDalamudPluginInterface pi, HubConnectionService hub, SyncOrchestrator sync)
        : base("Aethernet Settings###AethernetSettings")
    {
        _config = config; _pi = pi; _hub = hub; _sync = sync;
        Size = new Vector2(540, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.CollapsingHeader("Servers", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTextSetting("Auth server URL",  () => _config.AuthServerUrl, v => _config.AuthServerUrl = v);
            DrawTextSetting("Hub server URL",   () => _config.HubServerUrl,  v => _config.HubServerUrl  = v);
            DrawTextSetting("File server URL",  () => _config.FileServerUrl, v => _config.FileServerUrl = v);
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

            ImGui.InputTextWithHint("##recovery", "(optional) recovery secret to reuse", ref _registerRecoverySecret, 128);
            if (ImGui.Button("Register new account"))
                _ = RegisterAsync(_registerRecoverySecret);
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
            var c = _config.AccentColor;
            if (c is null || c.Length < 4) { _config.AccentColor = new[] { 0.20f, 0.78f, 0.35f, 1.0f }; c = _config.AccentColor; }
            var v = new System.Numerics.Vector4(c[0], c[1], c[2], c[3]);
            if (ImGui.ColorEdit4("Accent color", ref v))
                _config.AccentColor = new[] { v.X, v.Y, v.Z, v.W };

            var scale = _config.UiScale;
            if (ImGui.SliderFloat("UI scale", ref scale, 0.75f, 2.0f, "%.2fx"))
                _config.UiScale = scale;
        }

        ImGui.Separator();
        if (ImGui.Button("Save"))
        {
            _pi.SavePluginConfig(_config);
            IsOpen = false;
        }
    }

    private static void DrawTextSetting(string label, Func<string> get, Action<string> set)
    {
        var v = get();
        if (ImGui.InputText(label, ref v, 512)) set(v);
    }

    private async Task RegisterAsync(string? recoverySecret)
    {
        using var http = new HttpClient();
        var url  = $"{_config.AuthServerUrl.TrimEnd('/')}{Routes.Auth.Register}";
        var resp = await http.PostAsJsonAsync(url, new RegisterRequestDto(string.IsNullOrWhiteSpace(recoverySecret) ? null : recoverySecret));
        if (!resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponseDto>();
        if (body is null) return;
        _config.Uid       = body.UID;
        _config.SecretKey = body.SecretKey;
        _pi.SavePluginConfig(_config);
        await _hub.StartAsync();
    }
}
