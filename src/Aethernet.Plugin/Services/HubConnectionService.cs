using System.Net.Http;
using System.Net.Http.Json;
using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Configuration;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;  // for AddMessagePackProtocol
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Owns the SignalR connection to the Aethernet hub. Handles login via the auth service, JWT
/// refresh, connect/reconnect, and wires up server -> client method handlers.
/// </summary>
public sealed class HubConnectionService : IAsyncDisposable
{
    private readonly AethernetConfig _config;
    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<HubConnectionService> _log;
    private readonly PairManager _pairs;
    private readonly GroupManager _groups;
    private readonly CharacterDataApplier _applier;
    private readonly IObjectTable _objectTable;
    private readonly IClientState _clientState;
    private readonly IFramework _framework;

    private string? _lastPublishedIdent;

    private HubConnection? _hub;
    private readonly HttpClient _http = new();
    // Recreated on each StartAsync — StopAsync cancels it, so a stale cancelled CTS
    // would make every reconnect throw TaskCanceledException immediately.
    private CancellationTokenSource _cts = new();

    public HubConnectionState State => _hub?.State ?? HubConnectionState.Disconnected;
    public event Action<HubConnectionState>? StateChanged;

    public HubConnectionService(
        AethernetConfig config,
        IDalamudPluginInterface pi,
        ILogger<HubConnectionService> log,
        PairManager pairs,
        GroupManager groups,
        CharacterDataApplier applier,
        IObjectTable objectTable,
        IClientState clientState,
        IFramework framework)
    {
        _config = config; _pi = pi; _log = log;
        _pairs = pairs; _groups = groups; _applier = applier;
        _objectTable = objectTable; _clientState = clientState; _framework = framework;

        // Re-publish character ident on every event that could change what name@world we are
        // or invalidate the server's record of our ident:
        //   - TerritoryChanged: character re-instantiation, possible char swap
        //   - Login: covers the boot path where plugin loads BEFORE the user logs into XIV
        //     (very common since plugins start with XIVLauncher). Without this hook,
        //     PublishIdentAsync silently bails because _objectTable[0] is null pre-login,
        //     and nothing re-runs it after login finishes → blue dot forever.
        //   - Logout: clear the cached ident so the next login force-publishes even if
        //     someone re-logs to the same character.
        _clientState.TerritoryChanged += OnTerritoryChangedForIdent;
        _clientState.Login            += OnLogin;
        _clientState.Logout           += OnLogout;
    }

    private void OnTerritoryChangedForIdent(uint territoryId)
    {
        _ = PublishIdentAsync(force: true);
    }

    private void OnLogin()
    {
        _log.LogInformation("Local player logged in — publishing ident");
        _lastPublishedIdent = null;  // belt + suspenders: force the publish
        _ = PublishIdentAsync(force: true);
    }

    private void OnLogout(int type, int code)
    {
        _log.LogInformation("Local player logged out — clearing cached ident");
        _lastPublishedIdent = null;
    }

    /// <summary>
    /// Pushes the local player's "Name@WorldID" to the hub so paired clients can match
    /// us in their object tables for visibility-based mod application.
    /// </summary>
    public async Task PublishIdentAsync(bool force = false)
    {
        if (_hub?.State != HubConnectionState.Connected) return;
        try
        {
            // The player object lags Login and TerritoryChanged events by ~500ms, and may not
            // exist at all if the plugin started pre-login. Retry every 250ms for up to ~10s
            // to give the object table time to populate before we give up. Without the retry,
            // an early call here silently bails and nothing re-runs it → blue dot forever
            // until the user disables/re-enables the plugin.
            const int MaxAttempts = 40;          // ~10s wall time at 250ms cadence
            const int DelayMs     = 250;
            string? ident = null;
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                ident = await _framework.RunOnTick(() =>
                {
                    var local = _objectTable[0] as IPlayerCharacter;
                    return local is null ? null : $"{local.Name.TextValue}@{local.HomeWorld.RowId}";
                }, cancellationToken: _cts.Token).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(ident)) break;
                // If we're not even logged in, no point waiting for an object table that
                // can't possibly populate. Login event will re-call us.
                if (!_clientState.IsLoggedIn) return;
                await Task.Delay(DelayMs, _cts.Token);
            }
            if (string.IsNullOrEmpty(ident))
            {
                _log.LogWarning("PublishIdent: gave up after {Attempts} retries — local player still unavailable", MaxAttempts);
                return;
            }
            if (!force && ident == _lastPublishedIdent) return;
            await _hub.InvokeAsync(HubMethods.Server.UserSetIdent, ident, _cts.Token);
            _lastPublishedIdent = ident;
            _log.LogInformation("Published character ident: {Ident}", ident);
        }
        catch (OperationCanceledException) { /* shutting down — quiet */ }
        catch (Exception ex)
        {
            _log.LogWarning("PublishIdent failed: {Msg}", ex.Message);
        }
    }

    public async Task StartAsync()
    {
        if (string.IsNullOrEmpty(_config.Uid) || string.IsNullOrEmpty(_config.SecretKey))
        {
            _log.LogInformation("No credentials configured — staying offline. Open the Aethernet window to register.");
            return;
        }

        // Reset cancellation token in case StopAsync (e.g. Reconnect button) cancelled the previous one.
        if (_cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }

        try
        {
            var jwt = await EnsureAccessTokenAsync();
            _hub = new HubConnectionBuilder()
                .WithUrl($"{AethernetServers.HubUrl.TrimEnd('/')}{AethernetConstants.HubPath}?proto={AethernetConstants.ProtocolVersion}",
                    o => { o.AccessTokenProvider = () => Task.FromResult<string?>(jwt); })
                .AddMessagePackProtocol()
                .WithAutomaticReconnect(new[] { 0, 2, 5, 10, 15, 30 }.Select(s => TimeSpan.FromSeconds(s)).ToArray())
                .Build();

            RegisterClientHandlers(_hub);

            // NOTE: lambda params are named (not `_`) so we can still use `_` as a discard
            // inside the body. SignalR's Reconnected/Reconnecting params are string? (the
            // connection id); naming them `_` shadows the discard and `_ = SomeTaskCall()`
            // becomes a Task→string assignment that won't compile. Same shape as the
            // OnFrameworkTick(IFramework _) trap.
            _hub.Reconnecting += _ex => { Notify(HubConnectionState.Reconnecting); return Task.CompletedTask; };
            _hub.Reconnected  += _newId =>
            {
                Notify(HubConnectionState.Connected);
                // Server forgot our ident (and our online status) during the disconnect — must
                // re-publish so paired clients see us back. Without this, automatic reconnects
                // (transient network blips, server restarts) leave us "online but unidentified"
                // on the receiver side — same blue-dot symptom as the initial-login race.
                _lastPublishedIdent = null;
                _ = PublishIdentAsync(force: true);
                return Task.CompletedTask;
            };
            _hub.Closed       += _ex => { Notify(HubConnectionState.Disconnected); return Task.CompletedTask; };

            await _hub.StartAsync(_cts.Token);
            Notify(HubConnectionState.Connected);
            _log.LogInformation("Connected to Aethernet hub as {Uid}", _config.Uid);

            // Pull initial state.
            _pairs.Replace(await _hub.InvokeAsync<List<UserPairDto>>(HubMethods.Server.UserGetPairedClients, _cts.Token));
            _pairs.SetOnline(await _hub.InvokeAsync<List<OnlineUserIdentDto>>(HubMethods.Server.UserGetOnlinePairs, _cts.Token));

            // Publish our character ident so paired clients can spot us in their object tables.
            _ = PublishIdentAsync(force: true);

            // Safety-net: periodic re-sync of online-pair state. The initial UserGetOnlinePairs
            // + the server's Client_UserSendOnline broadcasts cover the happy path, but races
            // (broadcast arrives before our pair list, server presence eventually-consistent
            // across pod restarts, etc.) can leave a pair stuck in a wrong state until BOTH
            // sides bounce the plugin. Re-fetching every 30s caps the worst-case staleness.
            _ = RunOnlinePairsPollerAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to connect to Aethernet hub");
            Notify(HubConnectionState.Disconnected);
        }
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        if (_hub is not null) await _hub.DisposeAsync();
        _hub = null;
        Notify(HubConnectionState.Disconnected);
    }

    /// <summary>Convenience proxy — kept thin so call-sites stay readable.</summary>
    public Task<T> InvokeAsync<T>(string method, params object?[] args)
        => _hub?.InvokeCoreAsync<T>(method, args, _cts.Token) ?? throw new InvalidOperationException("not_connected");

    /// <summary>Background loop: every 30s, re-query the server's idea of which paired users
    /// are online and reconcile with our local PairManager. This is the safety net for any
    /// missed Client_UserSendOnline broadcast (race at connect, transient server-side
    /// presence inconsistency, etc.) so a paired user never gets stuck looking offline when
    /// they're actually online. Exits cleanly when _cts is cancelled (plugin shutdown).</summary>
    private async Task RunOnlinePairsPollerAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                if (_hub?.State != HubConnectionState.Connected) continue;
                try
                {
                    var snapshot = await _hub.InvokeAsync<List<OnlineUserIdentDto>>(
                        HubMethods.Server.UserGetOnlinePairs, ct).ConfigureAwait(false);
                    _pairs.SetOnline(snapshot);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    // Don't crash the loop on a single bad call — log and try again next tick.
                    _log.LogDebug("Online-pairs poller hiccup: {Msg}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    public Task InvokeAsync(string method, params object?[] args)
        => _hub?.InvokeCoreAsync(method, args, _cts.Token) ?? throw new InvalidOperationException("not_connected");

    // -----------------------------------------------------------------------
    // Auth
    // -----------------------------------------------------------------------

    private async Task<string> EnsureAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_config.AccessToken)
            && _config.AccessTokenExpiresAt is { } exp
            && exp > DateTime.UtcNow.AddMinutes(2))
            return _config.AccessToken!;

        var resp = await _http.PostAsJsonAsync(
            $"{AethernetServers.AuthUrl.TrimEnd('/')}{Routes.Auth.Login}",
            new LoginRequestDto(_config.Uid!, _config.SecretKey!),
            _cts.Token);
        resp.EnsureSuccessStatusCode();
        var login = await resp.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: _cts.Token)
                    ?? throw new InvalidOperationException("empty login response");

        _config.AccessToken          = login.AccessToken;
        _config.RefreshToken         = login.RefreshToken;
        _config.AccessTokenExpiresAt = DateTime.UtcNow.AddSeconds(login.ExpiresInSeconds - 30);
        _pi.SavePluginConfig(_config);
        return login.AccessToken;
    }

    // -----------------------------------------------------------------------
    // Server -> client dispatch
    // -----------------------------------------------------------------------

    private void RegisterClientHandlers(HubConnection hub)
    {
        hub.On<ServerMessageDto>(HubMethods.Client.ReceiveServerMessage, m =>
        {
            _log.LogInformation("server: [{Sev}] {Msg}", m.Severity, m.Message);
            return Task.CompletedTask;
        });

        hub.On<OnlineUserIdentDto>(HubMethods.Client.UserSendOnline, ident => {
            _log.LogInformation("Pair came online: UID={Uid} Ident={Ident}", ident.User.UID, ident.Ident);
            _pairs.MarkOnline(ident);
            return Task.CompletedTask;
        });
        hub.On<UserDto>(HubMethods.Client.UserSendOffline,                u  => { _pairs.MarkOffline(u);  return Task.CompletedTask; });

        hub.On<UserPairDto>(HubMethods.Client.UserAddPair,                p  => { _pairs.AddOrUpdate(p);  return Task.CompletedTask; });
        hub.On<string>(HubMethods.Client.UserRemovePair,                  uid=> { _pairs.Remove(uid);    return Task.CompletedTask; });
        hub.On<UserPermissionsDto>(HubMethods.Client.UserUpdatePairPermissions, p => { _pairs.UpdateOtherPermissions(p); return Task.CompletedTask; });
        hub.On<UserProfileDto>(HubMethods.Client.UserUpdateProfile,       p  => { _pairs.UpdateProfile(p); return Task.CompletedTask; });

        hub.On<OnlineUserCharaDataMessageDto>(HubMethods.Client.UserReceiveCharacterData, async msg =>
        {
            _log.LogInformation("Received character data from {Uid} dataVersion={Ver}", msg.User.UID, msg.CharacterData.DataVersion);
            try { await _applier.HandleAsync(msg, _cts.Token); }
            catch (Exception ex) { _log.LogError(ex, "applying character data from {Uid} failed", msg.User.UID); }
        });

        hub.On<GroupFullInfoDto>(HubMethods.Client.GroupSendFullInfo, g => { _groups.UpsertFull(g); return Task.CompletedTask; });
        hub.On<GroupInfoDto>(HubMethods.Client.GroupSendInfo,         g => { _groups.UpsertInfo(g); return Task.CompletedTask; });
        hub.On<GroupDto>(HubMethods.Client.GroupDelete,               g => { _groups.Remove(g);    return Task.CompletedTask; });
        hub.On<GroupPairFullInfoDto>(HubMethods.Client.GroupPairJoined, p => { _groups.MemberJoined(p); return Task.CompletedTask; });
        hub.On<GroupDto, UserDto>(HubMethods.Client.GroupPairLeft, (g, u) => { _groups.MemberLeft(g, u); return Task.CompletedTask; });
        hub.On<GroupPairUserInfoDto>(HubMethods.Client.GroupPairChangeUserInfo, i => { _groups.MemberInfo(i); return Task.CompletedTask; });
    }

    private void Notify(HubConnectionState state) { try { StateChanged?.Invoke(state); } catch { /* ignore */ } }

    public async ValueTask DisposeAsync()
    {
        // Unhook the IClientState event handlers we attached in the constructor; Dalamud holds
        // them indefinitely otherwise and the next plugin load gets a duplicate subscription.
        _clientState.TerritoryChanged -= OnTerritoryChangedForIdent;
        _clientState.Login            -= OnLogin;
        _clientState.Logout           -= OnLogout;
        _cts.Cancel();
        if (_hub is not null) await _hub.DisposeAsync();
        _http.Dispose();
        _cts.Dispose();
    }
}
