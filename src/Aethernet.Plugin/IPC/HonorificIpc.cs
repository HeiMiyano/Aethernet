using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

public sealed class HonorificIpc
{
    private const string ApiVersionLabel = "Honorific.ApiVersion";
    private const string GetCharacterTitleLabel = "Honorific.GetCharacterTitle";
    private const string SetCharacterTitleLabel = "Honorific.SetCharacterTitle";
    private const string ClearCharacterTitleLabel = "Honorific.ClearCharacterTitle";

    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<HonorificIpc> _log;

    public HonorificIpc(IDalamudPluginInterface pi, ILogger<HonorificIpc> log) { _pi = pi; _log = log; }

    public bool IsAvailable { get { try { return _pi.GetIpcSubscriber<int>(ApiVersionLabel).InvokeFunc() > 0; } catch { return false; } } }

    public string? GetTitleJson(int objectIndex)
    {
        try { return _pi.GetIpcSubscriber<int, string?>(GetCharacterTitleLabel).InvokeFunc(objectIndex); }
        catch (Exception ex) { _log.LogWarning(ex, "Honorific GetCharacterTitle failed"); return null; }
    }

    public void ApplyTitle(int objectIndex, string json)
    {
        try { _pi.GetIpcSubscriber<int, string, object>(SetCharacterTitleLabel).InvokeAction(objectIndex, json); }
        catch (Exception ex) { _log.LogError(ex, "Honorific SetCharacterTitle failed"); }
    }

    public void ClearTitle(int objectIndex)
    {
        try { _pi.GetIpcSubscriber<int, object>(ClearCharacterTitleLabel).InvokeAction(objectIndex); }
        catch (Exception ex) { _log.LogError(ex, "Honorific ClearCharacterTitle failed"); }
    }
}
