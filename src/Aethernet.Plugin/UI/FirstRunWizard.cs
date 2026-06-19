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

/// <summary>
/// Modal-ish wizard that walks a brand-new user through the bare minimum: register (or log
/// in with an existing UID/key), connect, optionally add a first pair. Server URLs are
/// baked into AethernetConfig defaults so the user never has to know about them.
/// Shown automatically when the plugin starts without credentials configured.
/// </summary>
public sealed class FirstRunWizard : Window
{
    private readonly AethernetConfig _config;
    private readonly IDalamudPluginInterface _pi;
    private readonly HubConnectionService _hub;

    // The Servers step was removed once we baked the production URLs into AethernetConfig
    // defaults — users no longer need to know about the underlying endpoints. Power users
    // who want to point at a different stack (e.g. localhost for dev) can still edit
    // AuthServerUrl / HubServerUrl / FileServerUrl in /aethernet settings.
    private enum Step { Welcome, Account, Pairing, Done }
    private Step _step = Step.Welcome;

    private string _existingUid = string.Empty;
    private string _existingKey = string.Empty;
    private string _recoverySecret = string.Empty;
    private string _firstPairUid = string.Empty;
    private string _statusLine = string.Empty;
    private bool _busy;
    private DateTime _copyFeedbackUntil = DateTime.MinValue;

    public FirstRunWizard(AethernetConfig config, IDalamudPluginInterface pi, HubConnectionService hub)
        : base("Welcome to Aethernet###AethernetWizard",
               ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking)
    {
        _config = config; _pi = pi; _hub = hub;
        Size = new Vector2(560, 440);
        SizeCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        ImGui.Text($"Step {(int)_step + 1} of 4");
        ImGui.Separator();

        switch (_step)
        {
            case Step.Welcome:  DrawWelcome();  break;
            case Step.Account:  DrawAccount();  break;
            case Step.Pairing:  DrawPairing();  break;
            case Step.Done:     DrawDone();     break;
        }
    }

    private void DrawWelcome()
    {
        ImGui.TextWrapped("Aethernet synchronizes Penumbra mods, Glamourer state, and related " +
                          "character-customization plugins between paired players.");
        ImGui.Spacing();
        ImGui.TextWrapped("Before you start, you should already have Penumbra and Glamourer " +
                          "installed and configured. Customize+, Honorific, SimpleHeels, Moodles, " +
                          "and Pet Names are optional and will be synced if present.");
        ImGui.Spacing();
        ImGui.TextDisabled("Aethernet is unofficial. Use responsibly.");
        BottomBar(nextEnabled: true);
    }

    private void DrawAccount()
    {
        if (!string.IsNullOrEmpty(_config.Uid))
        {
            ImGui.TextColored(new Vector4(0.2f,0.8f,0.3f,1f), $"Existing account: {_config.Uid}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy UID##wizard"))
            {
                ImGui.SetClipboardText(_config.Uid);
                _copyFeedbackUntil = DateTime.UtcNow.AddSeconds(2);
            }
            if (DateTime.UtcNow < _copyFeedbackUntil)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.3f, 1f), "copied!");
            }
            ImGui.TextDisabled("Share this UID with a friend so they can add you as a pair.");
            BottomBar(nextEnabled: true);
            return;
        }

        ImGui.TextWrapped("You can either register a brand-new account or recover an existing one.");
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Register new account", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.InputTextWithHint("##new_recovery", "(optional) bring your own recovery secret", ref _recoverySecret, 128);
            if (ImGui.Button("Register") && !_busy) _ = RegisterAsync();
        }
        if (ImGui.CollapsingHeader("Log in with an existing UID + secret key"))
        {
            ImGui.InputText("UID",        ref _existingUid, 32);
            ImGui.InputText("Secret key", ref _existingKey, 96);
            if (ImGui.Button("Log in") && !_busy) _ = LoginAsync();
        }
        if (ImGui.CollapsingHeader("Recover with a recovery secret"))
        {
            ImGui.InputText("Recovery secret", ref _recoverySecret, 128);
            if (ImGui.Button("Recover") && !_busy) _ = RecoverAsync();
        }
        if (!string.IsNullOrEmpty(_statusLine))
            ImGui.TextDisabled(_statusLine);

        BottomBar(nextEnabled: !string.IsNullOrEmpty(_config.Uid));
    }

    private void DrawPairing()
    {
        ImGui.TextWrapped("Optional: add your first pair now. You'll need a friend's UID.");

        // The hub may still be connecting if the user blew through Register → Next quickly.
        // Surface the state and gate the button so we don't throw "notconnected" mid-draw.
        var hubState = _hub.State;
        var connected = hubState == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected;
        if (!connected)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.7f, 0.3f, 1f),
                $"Hub: {hubState} — wait a moment for the connection to come up, or skip this step.");
        }

        ImGui.InputTextWithHint("##first_pair", "u-…", ref _firstPairUid, 32);

        if (!connected) ImGui.BeginDisabled();
        if (ImGui.Button("Add pair") && !string.IsNullOrWhiteSpace(_firstPairUid))
        {
            try
            {
                _ = _hub.InvokeAsync(HubMethods.Server.UserAddPair, new UserDto(_firstPairUid.Trim()));
                _firstPairUid = string.Empty;
                _statusLine = "Sent. They need to add you back for the pair to become active.";
            }
            catch (Exception ex)
            {
                _statusLine = $"Failed: {ex.Message}";
            }
        }
        if (!connected) ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(_statusLine))
            ImGui.TextDisabled(_statusLine);
        BottomBar(nextEnabled: true);
    }

    private void DrawDone()
    {
        ImGui.TextColored(new Vector4(0.2f,0.8f,0.3f,1f), "You're set up.");
        ImGui.Spacing();
        ImGui.TextWrapped("Open the main Aethernet window with /aethernet, or open settings with " +
                          "/aethernet settings. You can show this wizard again from settings if you ever need to.");
        ImGui.Spacing();
        if (ImGui.Button("Finish"))
        {
            IsOpen = false;
            _pi.SavePluginConfig(_config);
        }
    }

    private void BottomBar(bool nextEnabled)
    {
        ImGui.Spacing(); ImGui.Separator();
        if (_step != Step.Welcome && ImGui.Button("Back")) _step = (Step)((int)_step - 1);
        ImGui.SameLine();
        if (!nextEnabled) ImGui.BeginDisabled();
        if (ImGui.Button(_step == Step.Pairing ? "Finish" : "Next"))
            _step = (Step)Math.Min((int)Step.Done, (int)_step + 1);
        if (!nextEnabled) ImGui.EndDisabled();
    }

    // ---- network helpers ----

    private async Task RegisterAsync()
    {
        _busy = true; _statusLine = "Registering…";
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsJsonAsync($"{AethernetServers.AuthUrl.TrimEnd('/')}{Routes.Auth.Register}",
                new RegisterRequestDto(string.IsNullOrWhiteSpace(_recoverySecret) ? null : _recoverySecret));
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<RegisterResponseDto>();
            if (body is null) throw new InvalidOperationException("empty response");
            _config.Uid = body.UID; _config.SecretKey = body.SecretKey;
            _pi.SavePluginConfig(_config);
            _statusLine = $"Registered as {body.UID}. SAVE YOUR SECRET KEY — it is shown only once.";
            await _hub.StartAsync();
        }
        catch (Exception ex) { _statusLine = $"Failed: {ex.Message}"; }
        finally { _busy = false; }
    }

    private async Task LoginAsync()
    {
        _busy = true; _statusLine = "Connecting…";
        try
        {
            _config.Uid = _existingUid.Trim();
            _config.SecretKey = _existingKey.Trim();
            _pi.SavePluginConfig(_config);
            await _hub.StartAsync();
            _statusLine = "Connected.";
        }
        catch (Exception ex) { _statusLine = $"Failed: {ex.Message}"; }
        finally { _busy = false; }
    }

    private async Task RecoverAsync()
    {
        _busy = true; _statusLine = "Recovering…";
        try
        {
            using var http = new HttpClient();
            var resp = await http.PostAsJsonAsync($"{AethernetServers.AuthUrl.TrimEnd('/')}{Routes.Auth.Recover}",
                new Dictionary<string,string> { ["recovery_secret"] = _recoverySecret });
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<RegisterResponseDto>();
            if (body is null) throw new InvalidOperationException("empty response");
            _config.Uid = body.UID; _config.SecretKey = body.SecretKey;
            _pi.SavePluginConfig(_config);
            _statusLine = $"Recovered as {body.UID}.";
            await _hub.StartAsync();
        }
        catch (Exception ex) { _statusLine = $"Failed: {ex.Message}"; }
        finally { _busy = false; }
    }
}
