using Aethernet.API;
using Aethernet.API.Dto;
using Aethernet.Plugin.Configuration;
using Aethernet.Plugin.IPC;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Glue between "the user's appearance changed" and "push to paired users". Debounces frequent
/// changes so we don't spam the hub when the user is fiddling with Glamourer.
/// </summary>
public sealed class SyncOrchestrator : IDisposable
{
    private readonly AethernetConfig _config;
    private readonly IFramework _framework;
    private readonly CharacterDataCollector _collector;
    private readonly HubConnectionService _hub;
    private readonly PairManager _pairs;
    private readonly FileTransferService _transfer;
    private readonly ZoneObserver _zone;
    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly ILogger<SyncOrchestrator> _log;

    private DateTime _nextPushDue = DateTime.MaxValue;
    private string? _lastPushedHash;
    private readonly CancellationTokenSource _cts = new();

    public SyncOrchestrator(
        AethernetConfig config, IFramework framework,
        CharacterDataCollector collector, HubConnectionService hub,
        PairManager pairs, FileTransferService transfer, ZoneObserver zone,
        PenumbraIpc penumbra, GlamourerIpc glamourer,
        ILogger<SyncOrchestrator> log)
    {
        _config = config; _framework = framework; _collector = collector; _hub = hub;
        _pairs = pairs; _transfer = transfer; _zone = zone;
        _penumbra = penumbra; _glamourer = glamourer; _log = log;

        _pairs.PairsChanged += Schedule;
        _zone.QuietStateChanged += _ => Schedule();
        // Re-collect whenever Penumbra re-renders the local player or mod settings change.
        _penumbra.GameObjectRedrawn += idx => { if (idx == 0) { _log.LogDebug("trigger: penumbra redraw idx=0"); Schedule(); } };
        _penumbra.ModSettingChanged += () => { _log.LogDebug("trigger: penumbra mod setting changed"); Schedule(); };
        // Glamourer state changes fire on any wardrobe/customize edit — the immediate trigger
        // for live appearance sync.
        _glamourer.LocalStateChanged += () => { _log.LogDebug("trigger: glamourer state changed"); Schedule(); };

        // Safety-net periodic sync. Event-driven triggers above catch the obvious cases (mod
        // toggles, equipment redraws, zone changes) but miss things like Glamourer auto-design
        // shifts that happen without a Penumbra-side event. The hash check in PushOnceAsync
        // skips the actual hub call when nothing changed, so this is cheap on the wire.
        _ = PeriodicTickLoop();
    }

    private volatile bool _disposed;

    private async Task PeriodicTickLoop()
    {
        var interval = TimeSpan.FromSeconds(_config.PeriodicSyncIntervalSec > 0
            ? _config.PeriodicSyncIntervalSec : 30);
        try
        {
            while (!_cts.IsCancellationRequested && !_disposed)
            {
                await Task.Delay(interval, _cts.Token).ConfigureAwait(false);
                if (_cts.IsCancellationRequested || _disposed) return;
                Schedule(0);  // collect immediately; PushOnceAsync's hash check dedupes
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (System.IO.FileLoadException)
        {
            // Plugin's AssemblyLoadContext was torn down (e.g., reload while this task was mid-flight).
            // Lazy-loading any remaining dependency would also fail; bail quietly.
        }
    }

    public void Schedule() => Schedule(_config.DataPushDebounceMs);
    public void Schedule(int debounceMs)
    {
        if (_disposed || _cts.IsCancellationRequested) return;
        _nextPushDue = DateTime.UtcNow.AddMilliseconds(debounceMs);
        _ = TickLoop();
    }

    private bool _looping;
    private async Task TickLoop()
    {
        if (_looping || _disposed) return;
        _looping = true;
        try
        {
            while (DateTime.UtcNow < _nextPushDue)
            {
                await Task.Delay(50, _cts.Token).ConfigureAwait(false);
                if (_cts.IsCancellationRequested || _disposed) return;
            }
            _nextPushDue = DateTime.MaxValue;
            await PushOnceAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (System.IO.FileLoadException)
        {
            // ALC unloading — bail silently. See PeriodicTickLoop note.
        }
        catch (Exception ex) { _log.LogError(ex, "sync push failed"); }
        finally { _looping = false; }
    }

    private async Task PushOnceAsync(CancellationToken ct)
    {
        if (_hub.State != Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
        {
            _log.LogInformation("push skipped: hub state = {State}", _hub.State);
            return;
        }
        if (_zone.ShouldPauseSyncing)
        {
            _log.LogInformation("push skipped: outside quiet zone (PauseOutsideOfCities is on)");
            return;
        }

        var (data, hashToPath) = await _collector.CollectAsync(ct);
        _log.LogInformation("collected: {AppearanceCount} appearances, {FileCount} unique files, hash={Hash}",
            data.Appearances.Count, hashToPath.Count, data.DataHash);

        if (data.DataHash == _lastPushedHash)
        {
            _log.LogInformation("push skipped: data unchanged from last push");
            return;
        }

        await _transfer.UploadMissingAsync(hashToPath, ct);

        var recipients = _pairs.OnlineAndUnpaused.Select(p => p.Pair.User).ToList();
        if (recipients.Count == 0)
        {
            _log.LogInformation("push skipped: no online unpaused recipients");
            return;
        }

        _log.LogInformation("pushing dataVersion={Ver} to {RecipientCount} recipients",
            data.DataVersion, recipients.Count);
        await _hub.InvokeAsync(HubMethods.Server.UserPushData,
            new UserCharaDataMessageDto(recipients, data));
        _lastPushedHash = data.DataHash;
    }

    public void Dispose()
    {
        // Mark as disposed FIRST so any racing checks bail before they spawn new tasks.
        // The volatile bool is checked at every loop iteration entry and inside Schedule().
        _disposed = true;
        try { _cts.Cancel(); } catch { /* already cancelled / disposed */ }
        _pairs.PairsChanged -= Schedule;
    }
}
