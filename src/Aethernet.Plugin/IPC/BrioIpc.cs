using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

/// <summary>
/// Bridge to Brio for pose / scenario data. Like the other bridges, label strings track
/// upstream — adjust here if Brio rebrands.
/// </summary>
public sealed class BrioIpc
{
    private const string ApiVersionLabel   = "Brio.ApiVersion";
    private const string GetPoseJsonLabel  = "Brio.GetActorPose";
    private const string ApplyPoseLabel    = "Brio.SetActorPose";
    private const string ClearPoseLabel    = "Brio.ResetActor";

    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<BrioIpc> _log;

    public BrioIpc(IDalamudPluginInterface pi, ILogger<BrioIpc> log) { _pi = pi; _log = log; }

    public bool IsAvailable
    {
        get { try { return _pi.GetIpcSubscriber<int>(ApiVersionLabel).InvokeFunc() > 0; } catch { return false; } }
    }

    public string? GetPoseJson(int objectIndex)
    {
        try { return _pi.GetIpcSubscriber<int, string?>(GetPoseJsonLabel).InvokeFunc(objectIndex); }
        catch (Exception ex) { _log.LogWarning(ex, "Brio GetActorPose failed"); return null; }
    }

    public void ApplyPose(int objectIndex, string json)
    {
        try { _pi.GetIpcSubscriber<int, string, object>(ApplyPoseLabel).InvokeAction(objectIndex, json); }
        catch (Exception ex) { _log.LogError(ex, "Brio SetActorPose failed"); }
    }

    public void ClearPose(int objectIndex)
    {
        try { _pi.GetIpcSubscriber<int, object>(ClearPoseLabel).InvokeAction(objectIndex); }
        catch (Exception ex) { _log.LogError(ex, "Brio ResetActor failed"); }
    }
}
