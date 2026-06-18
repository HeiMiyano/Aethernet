using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

/// <summary>Bridge to Customize+ for body/face scaling profiles.</summary>
public sealed class CustomizePlusIpc
{
    private const string ApiVersionLabel = "CustomizePlus.General.GetApiVersion";
    private const string GetActiveProfileLabel = "CustomizePlus.Profile.GetActiveProfile";
    private const string SetTemporaryProfileLabel = "CustomizePlus.Profile.SetTemporaryProfileOnCharacter";
    private const string DeleteTemporaryProfileLabel = "CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter";

    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<CustomizePlusIpc> _log;

    public CustomizePlusIpc(IDalamudPluginInterface pi, ILogger<CustomizePlusIpc> log)
    {
        _pi = pi; _log = log;
    }

    public bool IsAvailable
    {
        get { try { return _pi.GetIpcSubscriber<(int, int)>(ApiVersionLabel).InvokeFunc().Item1 > 0; } catch { return false; } }
    }

    // Circuit breaker for GetProfileJson. Customize+'s API signature drifts between versions and
    // the failed call appears to trigger Penumbra redraws on the caller, which then schedule
    // another sync push → another failed call → feedback loop. After N consecutive failures we
    // stop trying for a cool-off period, breaking the loop. We also probe two candidate signatures
    // to handle both the legacy <int, string?> and newer <int, (int, string?)> tuple-return shapes.
    private int _profileFailCount;
    private DateTime _profileNextRetryAt = DateTime.MinValue;
    private Func<int, string?>? _profileInvoker;
    private const int ProfileFailureThreshold = 3;
    private static readonly TimeSpan ProfileCoolOff = TimeSpan.FromSeconds(60);

    public string? GetProfileJson(int objectIndex)
    {
        if (DateTime.UtcNow < _profileNextRetryAt) return null;

        try
        {
            _profileInvoker ??= ResolveProfileInvoker();
            if (_profileInvoker is null) { TripBreaker("no shape bound"); return null; }
            var result = _profileInvoker(objectIndex);
            _profileFailCount = 0;  // success — reset
            return result;
        }
        catch (Exception ex)
        {
            TripBreaker(ex.Message);
            return null;
        }
    }

    private Func<int, string?>? ResolveProfileInvoker()
    {
        var attempts = new (string Shape, Func<Func<int, string?>> Build)[]
        {
            ("<int, (int, string?)> tuple-return", () =>
            {
                var s = _pi.GetIpcSubscriber<int, (int Status, string? Json)>(GetActiveProfileLabel);
                return idx => s.InvokeFunc(idx).Json;
            }),
            ("<int, string?> legacy", () =>
            {
                var s = _pi.GetIpcSubscriber<int, string?>(GetActiveProfileLabel);
                return idx => s.InvokeFunc(idx);
            }),
        };
        foreach (var (shape, build) in attempts)
        {
            try
            {
                var inv = build();
                _ = inv(0);  // probe with the local player; if this throws, try next shape
                _log.LogInformation("Customize+ GetActiveProfile bound with {Shape}", shape);
                return inv;
            }
            catch (Exception ex) { _log.LogDebug("Customize+ shape {Shape} rejected: {Msg}", shape, ex.Message); }
        }
        return null;
    }

    private void TripBreaker(string reason)
    {
        _profileFailCount++;
        if (_profileFailCount >= ProfileFailureThreshold)
        {
            _profileNextRetryAt = DateTime.UtcNow + ProfileCoolOff;
            _log.LogWarning("Customize+ GetActiveProfile failed {N} times ({Reason}); cooling off until {Until:HH:mm:ss}",
                _profileFailCount, reason, _profileNextRetryAt);
            _profileFailCount = 0;  // reset counter for next window
        }
        else
        {
            _log.LogDebug("Customize+ GetActiveProfile failed ({Reason}); attempt {N}/{Threshold}",
                reason, _profileFailCount, ProfileFailureThreshold);
        }
    }

    public void ApplyProfile(int objectIndex, string profileJson)
    {
        try { _pi.GetIpcSubscriber<int, string, object>(SetTemporaryProfileLabel)
                   .InvokeAction(objectIndex, profileJson); }
        catch (Exception ex) { _log.LogError(ex, "Customize+ SetTemporaryProfile failed"); }
    }

    public void RemoveProfile(int objectIndex)
    {
        try { _pi.GetIpcSubscriber<int, object>(DeleteTemporaryProfileLabel).InvokeAction(objectIndex); }
        catch (Exception ex) { _log.LogError(ex, "Customize+ DeleteTemporaryProfile failed"); }
    }
}
