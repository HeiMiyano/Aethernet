using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

public sealed class MoodlesIpc
{
    private const string ApiVersionLabel = "Moodles.ApiVersion";
    private const string GetStatusManagerLabel = "Moodles.GetStatusManagerByPtr";
    private const string ApplyStatusManagerLabel = "Moodles.SetStatusManagerByPtr";

    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<MoodlesIpc> _log;

    public MoodlesIpc(IDalamudPluginInterface pi, ILogger<MoodlesIpc> log) { _pi = pi; _log = log; }

    public bool IsAvailable { get { try { return _pi.GetIpcSubscriber<int>(ApiVersionLabel).InvokeFunc() > 0; } catch { return false; } } }

    public string? GetStatusJson(nint address)
    {
        try { return _pi.GetIpcSubscriber<nint, string?>(GetStatusManagerLabel).InvokeFunc(address); }
        catch (Exception ex) { _log.LogWarning(ex, "Moodles GetStatusManager failed"); return null; }
    }

    public void ApplyStatus(nint address, string json)
    {
        try { _pi.GetIpcSubscriber<nint, string, object>(ApplyStatusManagerLabel).InvokeAction(address, json); }
        catch (Exception ex) { _log.LogError(ex, "Moodles SetStatusManager failed"); }
    }
}
