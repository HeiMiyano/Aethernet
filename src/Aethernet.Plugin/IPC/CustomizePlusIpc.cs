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

    public string? GetProfileJson(int objectIndex)
    {
        try { return _pi.GetIpcSubscriber<int, string?>(GetActiveProfileLabel).InvokeFunc(objectIndex); }
        catch (Exception ex) { _log.LogWarning(ex, "Customize+ GetActiveProfile failed"); return null; }
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
