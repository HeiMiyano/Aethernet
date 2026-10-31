using System.Net.Http;
using System.Net.Http.Json;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Configuration;
using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Runs "Sign in with Discord" / "Link Discord": asks the auth service for a Discord authorize
/// URL, opens it in the user's browser, then polls until the browser leg finishes and stores the
/// resulting credentials. The browser never sees the secret key — it comes back only through
/// the poll, which needs a token only this plugin instance holds.
///
/// UI reads <see cref="Phase"/> / <see cref="StatusMessage"/> every frame; nothing here touches ImGui.
/// </summary>
public sealed class DiscordSignInService : IDisposable
{
    public enum SignInPhase { Idle, Starting, WaitingForBrowser, Succeeded, Failed }

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly AethernetConfig _config;
    private readonly IDalamudPluginInterface _pi;
    private readonly HubConnectionService _hub;
    private readonly ILogger<DiscordSignInService> _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private CancellationTokenSource? _cts;

    public SignInPhase Phase { get; private set; } = SignInPhase.Idle;
    public string StatusMessage { get; private set; } = string.Empty;
    /// <summary>Kept so the UI can offer "browser didn't open? copy the link".</summary>
    public string? AuthorizeUrl { get; private set; }
    public bool IsBusy => Phase is SignInPhase.Starting or SignInPhase.WaitingForBrowser;

    public DiscordSignInService(
        AethernetConfig config, IDalamudPluginInterface pi, HubConnectionService hub, ILogger<DiscordSignInService> log)
    {
        _config = config; _pi = pi; _hub = hub; _log = log;
    }

    /// <summary>Create an account, or sign in to the one already linked to this Discord user
    /// (new PC / lost key — the server issues a fresh secret key either way).</summary>
    public void StartSignIn() => Start(DiscordAuthModes.SignIn, null, null);

    /// <summary>Attach Discord to the pre-Discord account currently in config.</summary>
    public void StartLink() => Start(DiscordAuthModes.Link, _config.Uid, _config.SecretKey);

    public void Cancel()
    {
        _cts?.Cancel();
        if (IsBusy) Set(SignInPhase.Idle, "Cancelled.");
    }

    private void Start(string mode, string? uid, string? secretKey)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        AuthorizeUrl = null;
        Set(SignInPhase.Starting, "Contacting the Aethernet server…");
        _ = RunAsync(mode, uid, secretKey, _cts.Token);
    }

    private async Task RunAsync(string mode, string? uid, string? secretKey, CancellationToken ct)
    {
        var baseUrl = AethernetServers.AuthUrl.TrimEnd('/');
        try
        {
            var beginResp = await _http.PostAsJsonAsync($"{baseUrl}{Routes.Auth.DiscordBegin}",
                new DiscordBeginRequestDto(mode, uid, secretKey), ct);
            if (!beginResp.IsSuccessStatusCode)
            {
                var err = await ReadErrorAsync(beginResp, ct);
                if (mode == DiscordAuthModes.Link && err?.Error == AuthErrorCodes.AlreadyLinked)
                {
                    // Nothing to do — just remember it so the Link button goes away.
                    _config.DiscordLinked = true;
                    _pi.SavePluginConfig(_config);
                    Set(SignInPhase.Succeeded, "This account is already linked to Discord.");
                    return;
                }
                Set(SignInPhase.Failed, !string.IsNullOrWhiteSpace(err?.Message)
                    ? err.Message
                    : $"Server returned {(int)beginResp.StatusCode} {beginResp.ReasonPhrase}.");
                return;
            }
            var begin = await beginResp.Content.ReadFromJsonAsync<DiscordBeginResponseDto>(cancellationToken: ct)
                        ?? throw new InvalidOperationException("empty response from server");

            AuthorizeUrl = begin.AuthorizeUrl;
            Set(SignInPhase.WaitingForBrowser, "Waiting for you to click Authorize in your browser…");
            try
            {
                Dalamud.Utility.Util.OpenLink(begin.AuthorizeUrl);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Couldn't open the browser automatically: {Msg}", ex.Message);
                StatusMessage = "Couldn't open your browser — use Copy link and paste it into a browser.";
            }

            var deadline = DateTime.UtcNow.AddSeconds(begin.ExpiresInSeconds);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollInterval, ct);

                DiscordPollResponseDto? poll;
                try
                {
                    var pollResp = await _http.PostAsJsonAsync($"{baseUrl}{Routes.Auth.DiscordPoll}",
                        new DiscordPollRequestDto(begin.PollToken), ct);
                    if (!pollResp.IsSuccessStatusCode) continue;   // transient — keep polling
                    poll = await pollResp.Content.ReadFromJsonAsync<DiscordPollResponseDto>(cancellationToken: ct);
                }
                catch (HttpRequestException ex)
                {
                    _log.LogDebug("Discord sign-in poll hiccup: {Msg}", ex.Message);
                    continue;
                }

                if (poll is null || poll.Status == DiscordPollStatus.Pending) continue;
                if (poll.Status == DiscordPollStatus.Failed)
                {
                    Set(SignInPhase.Failed, poll.Message ?? "Sign-in failed.");
                    return;
                }

                await ApplyAsync(poll);
                return;
            }

            Set(SignInPhase.Failed, "Timed out waiting for Discord. Click the button to try again.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* Cancel() */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "Discord sign-in failed");
            Set(SignInPhase.Failed, $"Couldn't reach the Aethernet server: {ex.Message}");
        }
    }

    private async Task ApplyAsync(DiscordPollResponseDto poll)
    {
        if (!string.IsNullOrEmpty(poll.UID) && !string.IsNullOrEmpty(poll.SecretKey))
        {
            _config.Uid       = poll.UID;
            _config.SecretKey = poll.SecretKey;
        }
        // Signing in may switch accounts or rotate the key, and linking changes what /login
        // returns — either way the cached JWT is stale. Force a fresh login on reconnect.
        _config.AccessToken          = null;
        _config.RefreshToken         = null;
        _config.AccessTokenExpiresAt = null;
        _config.DiscordLinked        = true;
        _pi.SavePluginConfig(_config);

        Set(SignInPhase.Succeeded, poll.Message ?? "Done.");
        _log.LogInformation("Discord sign-in complete for {Uid} (existing account: {Existing})", _config.Uid, poll.ExistingAccount);

        await _hub.StopAsync();
        await _hub.StartAsync();
    }

    private static async Task<AuthErrorDto?> ReadErrorAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadFromJsonAsync<AuthErrorDto>(cancellationToken: ct); }
        catch { return null; /* not our JSON error shape */ }
    }

    private void Set(SignInPhase phase, string message)
    {
        Phase = phase;
        StatusMessage = message;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _http.Dispose();
    }
}
