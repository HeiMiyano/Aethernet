using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

public sealed class HonorificIpc
{
    private const string ApiVersionLabel = "Honorific.ApiVersion";
    private const string GetCharacterTitleLabel = "Honorific.GetCharacterTitle";
    private const string SetCharacterTitleLabel = "Honorific.SetCharacterTitle";
    private const string ClearCharacterTitleLabel = "Honorific.ClearCharacterTitle";
    private const string GetLocalCharacterTitleLabel = "Honorific.GetLocalCharacterTitle";

    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<HonorificIpc> _log;

    public HonorificIpc(IDalamudPluginInterface pi, ILogger<HonorificIpc> log) { _pi = pi; _log = log; }

    // Honorific registers ApiVersion as (uint Major, uint Minor). Asking for a plain int makes
    // Dalamud's IPC throw on the type mismatch, which silently marked Honorific as unavailable.
    private bool? _lastAvailable;

    public bool IsAvailable
    {
        get
        {
            bool ok; string detail;
            try { var v = _pi.GetIpcSubscriber<(uint, uint)>(ApiVersionLabel).InvokeFunc(); ok = v.Item1 > 0; detail = $"ApiVersion={v.Item1}.{v.Item2}"; }
            catch (Exception ex) { ok = false; detail = ex.GetType().Name + ": " + ex.Message; }
            if (_lastAvailable != ok)
            {
                _lastAvailable = ok;
                _log.LogInformation("Honorific IPC available={Ok} ({Detail})", ok, detail);
            }
            return ok;
        }
    }

    public string? GetTitleJson(int objectIndex)
    {
        try
        {
            var json = _pi.GetIpcSubscriber<int, string?>(GetCharacterTitleLabel).InvokeFunc(objectIndex);
            if (IsEmpty(json))
                json = _pi.GetIpcSubscriber<string?>(GetLocalCharacterTitleLabel).InvokeFunc();
            var result = IsEmpty(json) ? null : json;
            if (result != _lastLoggedTitle)
            {
                _lastLoggedTitle = result;
                _log.LogInformation("Honorific local title for idx={Idx}: {Title}", objectIndex, result ?? "(none)");
            }
            return result;
        }
        catch (Exception ex) { _log.LogWarning(ex, "Honorific GetCharacterTitle failed"); return null; }
    }

    private string? _lastLoggedTitle;

    private static bool IsEmpty(string? json) =>
        string.IsNullOrWhiteSpace(json) || json.Trim() == "null";

    public void ApplyTitle(int objectIndex, string json)
    {
        try
        {
            _pi.GetIpcSubscriber<int, string, object>(SetCharacterTitleLabel).InvokeAction(objectIndex, json);
            _log.LogInformation("Honorific SetCharacterTitle applied to idx={Idx}: {Json}", objectIndex, json);
        }
        catch (Exception ex) { _log.LogError(ex, "Honorific SetCharacterTitle failed"); }
    }

    public void ClearTitle(int objectIndex)
    {
        try { _pi.GetIpcSubscriber<int, object>(ClearCharacterTitleLabel).InvokeAction(objectIndex); }
        catch (Exception ex) { _log.LogError(ex, "Honorific ClearCharacterTitle failed"); }
    }
}
