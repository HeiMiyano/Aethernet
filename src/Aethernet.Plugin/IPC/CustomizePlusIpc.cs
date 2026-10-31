using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

/// <summary>
/// Bridge to Customize+ for body/face scaling profiles.
///
/// Signatures verified against CustomizePlus.dll 2.2.1.3 (CustomizePlus.Api.CustomizePlusIpc):
///   Profile.GetActiveProfileIdOnCharacter (ushort gameObjectIndex)            → (int, Guid?)
///   Profile.GetByUniqueId                 (Guid uniqueId)                     → (int, string)
///   Profile.SetTemporaryProfileOnCharacter(ushort gameObjectIndex, string json) → (int, Guid?)
///   Profile.DeleteTemporaryProfileOnCharacter(ushort gameObjectIndex)         → int
/// The int is CustomizePlus.Api.Enums.ErrorCode. Older Aethernet builds called a
/// "Profile.GetActiveProfile" label that no longer exists, and passed int indices / invoked the
/// setters as actions, so Customize+ data never synced in either direction.
/// </summary>
public sealed class CustomizePlusIpc
{
    private const string ApiVersionLabel              = "CustomizePlus.General.GetApiVersion";
    private const string GetActiveProfileIdLabel      = "CustomizePlus.Profile.GetActiveProfileIdOnCharacter";
    private const string GetProfileByUniqueIdLabel    = "CustomizePlus.Profile.GetByUniqueId";
    private const string SetTemporaryProfileLabel     = "CustomizePlus.Profile.SetTemporaryProfileOnCharacter";
    private const string DeleteTemporaryProfileLabel  = "CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter";

    // CustomizePlus.Api.Enums.ErrorCode
    private const int Success          = 0;
    private const int InvalidCharacter = 1;
    private const int ProfileNotFound  = 3;

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

    // Circuit breaker for GetProfileJson. Kept from the signature-drift era: a call that throws
    // every collect used to feed a redraw → sync → failed-call loop. After N consecutive real
    // failures we stop trying for a cool-off period. "No profile on this character" is NOT a
    // failure and never trips it.
    private int _profileFailCount;
    private DateTime _profileNextRetryAt = DateTime.MinValue;
    private const int ProfileFailureThreshold = 3;
    private static readonly TimeSpan ProfileCoolOff = TimeSpan.FromSeconds(60);

    /// <summary>Returns the JSON of the Customize+ profile active on the given object, or null if
    /// it has none (or Customize+ is unavailable / cooling off).</summary>
    public string? GetProfileJson(int objectIndex)
    {
        if (DateTime.UtcNow < _profileNextRetryAt) return null;

        try
        {
            var (status, profileId) = _pi.GetIpcSubscriber<ushort, (int, Guid?)>(GetActiveProfileIdLabel)
                                         .InvokeFunc((ushort)objectIndex);
            if (status is ProfileNotFound or InvalidCharacter || (status == Success && profileId is null))
            {
                _profileFailCount = 0;
                return null; // no profile, or character not ready yet — both normal
            }
            if (status != Success || profileId is null)
            {
                TripBreaker($"GetActiveProfileIdOnCharacter returned {status}");
                return null;
            }

            var (getStatus, json) = _pi.GetIpcSubscriber<Guid, (int, string)>(GetProfileByUniqueIdLabel)
                                       .InvokeFunc(profileId.Value);
            if (getStatus == ProfileNotFound) { _profileFailCount = 0; return null; } // removed between calls
            if (getStatus != Success)
            {
                TripBreaker($"GetByUniqueId returned {getStatus}");
                return null;
            }

            _profileFailCount = 0;
            return string.IsNullOrEmpty(json) ? null : json;
        }
        catch (Exception ex)
        {
            TripBreaker(ex.Message);
            return null;
        }
    }

    private void TripBreaker(string reason)
    {
        _profileFailCount++;
        if (_profileFailCount >= ProfileFailureThreshold)
        {
            _profileNextRetryAt = DateTime.UtcNow + ProfileCoolOff;
            _log.LogWarning("Customize+ profile read failed {N} times ({Reason}); cooling off until {Until:HH:mm:ss}",
                _profileFailCount, reason, _profileNextRetryAt);
            _profileFailCount = 0;  // reset counter for next window
        }
        else
        {
            _log.LogDebug("Customize+ profile read failed ({Reason}); attempt {N}/{Threshold}",
                reason, _profileFailCount, ProfileFailureThreshold);
        }
    }

    /// <summary>Applies a paired player's profile JSON to their character as a temporary profile.</summary>
    public void ApplyProfile(int objectIndex, string profileJson)
    {
        try
        {
            var (status, _) = _pi.GetIpcSubscriber<ushort, string, (int, Guid?)>(SetTemporaryProfileLabel)
                                 .InvokeFunc((ushort)objectIndex, profileJson);
            if (status != Success)
                _log.LogWarning("Customize+ SetTemporaryProfileOnCharacter(idx={Idx}) returned {Status}", objectIndex, status);
        }
        catch (Exception ex) { _log.LogError(ex, "Customize+ SetTemporaryProfileOnCharacter failed"); }
    }

    /// <summary>Removes any temporary profile we applied to the given character. Safe to call when
    /// there isn't one.</summary>
    public void RemoveProfile(int objectIndex)
    {
        try
        {
            var status = _pi.GetIpcSubscriber<ushort, int>(DeleteTemporaryProfileLabel).InvokeFunc((ushort)objectIndex);
            if (status is not (Success or ProfileNotFound or InvalidCharacter))
                _log.LogDebug("Customize+ DeleteTemporaryProfileOnCharacter(idx={Idx}) returned {Status}", objectIndex, status);
        }
        catch (Exception ex) { _log.LogError(ex, "Customize+ DeleteTemporaryProfileOnCharacter failed"); }
    }
}
