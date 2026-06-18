using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

public sealed class HeelsIpc
{
    private const string ApiVersionLabel = "SimpleHeels.ApiVersion";
    private const string GetLocalOffsetLabel = "SimpleHeels.GetLocalPlayer";
    private const string RegisterPlayerOffsetLabel = "SimpleHeels.RegisterPlayer";
    private const string UnregisterPlayerOffsetLabel = "SimpleHeels.UnregisterPlayer";

    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<HeelsIpc> _log;

    public HeelsIpc(IDalamudPluginInterface pi, ILogger<HeelsIpc> log) { _pi = pi; _log = log; }

    public bool IsAvailable { get { try { return _pi.GetIpcSubscriber<(int, int)>(ApiVersionLabel).InvokeFunc().Item1 > 0; } catch { return false; } } }

    public string? GetLocalOffsetJson()
    {
        try { return _pi.GetIpcSubscriber<string?>(GetLocalOffsetLabel).InvokeFunc(); }
        catch (Exception ex) { _log.LogWarning(ex, "Heels GetLocalPlayer failed"); return null; }
    }

    public void RegisterOffset(int objectIndex, string json)
    {
        try { _pi.GetIpcSubscriber<int, string, object>(RegisterPlayerOffsetLabel).InvokeAction(objectIndex, json); }
        catch (Exception ex) { _log.LogError(ex, "Heels RegisterPlayer failed"); }
    }

    public void UnregisterOffset(int objectIndex)
    {
        try { _pi.GetIpcSubscriber<int, object>(UnregisterPlayerOffsetLabel).InvokeAction(objectIndex); }
        catch (Exception ex) { _log.LogError(ex, "Heels UnregisterPlayer failed"); }
    }
}
