using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

public sealed class PetNamesIpc
{
    private const string ApiVersionLabel = "PetRenamer.ApiVersion";
    private const string GetLocalNamesLabel = "PetRenamer.GetLocalNickNames";
    private const string SetNamesForPlayerLabel = "PetRenamer.SetPlayerData";
    private const string ClearNamesForPlayerLabel = "PetRenamer.ClearPlayerData";

    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<PetNamesIpc> _log;

    public PetNamesIpc(IDalamudPluginInterface pi, ILogger<PetNamesIpc> log) { _pi = pi; _log = log; }

    public bool IsAvailable { get { try { return _pi.GetIpcSubscriber<int>(ApiVersionLabel).InvokeFunc() > 0; } catch { return false; } } }

    public string? GetLocalNamesJson()
    {
        try { return _pi.GetIpcSubscriber<string?>(GetLocalNamesLabel).InvokeFunc(); }
        catch (Exception ex) { _log.LogWarning(ex, "PetNames GetLocalNickNames failed"); return null; }
    }

    public void ApplyNames(string identKey, string json)
    {
        try { _pi.GetIpcSubscriber<string, string, object>(SetNamesForPlayerLabel).InvokeAction(identKey, json); }
        catch (Exception ex) { _log.LogError(ex, "PetNames SetPlayerData failed"); }
    }

    public void ClearNames(string identKey)
    {
        try { _pi.GetIpcSubscriber<string, object>(ClearNamesForPlayerLabel).InvokeAction(identKey); }
        catch (Exception ex) { _log.LogError(ex, "PetNames ClearPlayerData failed"); }
    }
}
