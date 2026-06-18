using Dalamud.Plugin;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

/// <summary>
/// Bridge to Glamourer 1.6.x. Signatures resolved against the live Glamourer.Api.dll:
///   GetStateBase64: (int objectIndex, uint lockKey) → (int status, string? base64)
///
/// Glamourer wraps every Func return in a (status, payload) tuple where `status` is the
/// GlamourerApiEc enum value (int-backed). Non-zero status means failure; we just consume
/// the payload and log if status != 0.
///
/// ApplyState / RevertState are probed on first use because their exact return-tuple
/// shape isn't documented; the resolver caches the winning shape per process lifetime.
/// </summary>
public sealed class GlamourerIpc : IDisposable
{
    private const string ApiVersionsLabel       = "Glamourer.ApiVersions";
    private const string GetStateBase64Label    = "Glamourer.GetStateBase64";
    private const string ApplyStateLabel        = "Glamourer.ApplyState";
    private const string RevertStateLabel       = "Glamourer.RevertState";
    private const string StateChangedLabel      = "Glamourer.StateChangedWithType";
    private const string StateFinalizedLabel    = "Glamourer.StateFinalized";
    private const string GPoseChangedLabel      = "Glamourer.GPoseChanged";

    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<GlamourerIpc> _log;
    private readonly List<IDisposable> _subscriptions = new();

    /// <summary>Fires when the local player's Glamourer state changes (any wardrobe/customize edit).
    /// This is the trigger for re-collecting and pushing to paired friends.</summary>
    public event Action? LocalStateChanged;

    public GlamourerIpc(IDalamudPluginInterface pi, ILogger<GlamourerIpc> log)
    {
        _pi = pi; _log = log;
        TrySubscribeToEvents();
    }

    private void TrySubscribeToEvents()
    {
        // Glamourer's state-change events typically signal (nint actorAddress, ChangeType type).
        // We probe a few candidate shapes since the exact signature drifts between versions.
        TrySubscribe(StateChangedLabel, "<nint, StateChangeType>", () => {
            var s = _pi.GetIpcSubscriber<nint, int, object>(StateChangedLabel);
            Action<nint, int> h = (_, _) => { try { LocalStateChanged?.Invoke(); } catch { } };
            s.Subscribe(h);
            return new Unsub(() => s.Unsubscribe(h));
        });
        TrySubscribe(StateFinalizedLabel, "<nint>", () => {
            var s = _pi.GetIpcSubscriber<nint, object>(StateFinalizedLabel);
            Action<nint> h = _ => { try { LocalStateChanged?.Invoke(); } catch { } };
            s.Subscribe(h);
            return new Unsub(() => s.Unsubscribe(h));
        });
        TrySubscribe(GPoseChangedLabel, "<bool>", () => {
            var s = _pi.GetIpcSubscriber<bool, object>(GPoseChangedLabel);
            Action<bool> h = _ => { try { LocalStateChanged?.Invoke(); } catch { } };
            s.Subscribe(h);
            return new Unsub(() => s.Unsubscribe(h));
        });
    }

    private void TrySubscribe(string label, string shape, Func<IDisposable> build)
    {
        try
        {
            _subscriptions.Add(build());
            _log.LogInformation("Subscribed to {Label} {Shape}", label, shape);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Subscribe {Label} {Shape} failed: {Msg}", label, shape, ex.Message);
        }
    }

    private sealed class Unsub : IDisposable
    {
        private readonly Action _dispose;
        public Unsub(Action d) { _dispose = d; }
        public void Dispose() { try { _dispose(); } catch { } }
    }

    public void Dispose()
    {
        foreach (var s in _subscriptions) s.Dispose();
        _subscriptions.Clear();
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                var ver = _pi.GetIpcSubscriber<(int Major, int Minor)>(ApiVersionsLabel).InvokeFunc();
                return ver.Major > 0;
            }
            catch { return false; }
        }
    }

    public string? GetStateBase64(int objectIndex)
    {
        try
        {
            var (status, base64) = _pi
                .GetIpcSubscriber<int, uint, (int, string?)>(GetStateBase64Label)
                .InvokeFunc(objectIndex, 0u);
            if (status != 0)
                _log.LogDebug("Glamourer.GetStateBase64 status={Status} for objectIndex={Idx}", status, objectIndex);
            return base64;
        }
        catch (Exception ex)
        {
            _log.LogWarning("Glamourer.GetStateBase64 failed: {Msg}", ex.Message);
            return null;
        }
    }

    // ---- ApplyState ---------------------------------------------------------
    private Action<string, int>? _applyInvoker;
    public void ApplyState(string state, int objectIndex)
    {
        _applyInvoker ??= ResolveApplyInvoker();
        if (_applyInvoker is null) return;
        try { _applyInvoker(state, objectIndex); }
        catch (Exception ex) { _log.LogError("Glamourer.ApplyState failed: {Msg}", ex.Message); }
    }

    private Action<string, int>? ResolveApplyInvoker()
    {
        // Reflected against Glamourer.Api 1.6.1.7:
        //   GlamourerApiEc Invoke(string base64State, int objectIndex, uint key, ApplyFlag flags)
        // ApplyFlag (ulong bitmask): Once=1, Equipment=2, Customization=4, Lock=8.
        // Use StateDefault = 14 (Equipment | Customization | Lock) — what Mare uses to fully
        // apply a synced design and prevent the local Glamourer auto-design from overwriting it.
        // Passing 0 (which the previous version did) applies neither Equipment nor Customization.
        const ulong ApplyFlagsStateDefault = 14ul;
        var attempts = new (string Shape, Func<Action<string, int>> Build)[]
        {
            ("<string, int, uint, ulong, int>(s, idx, 0, StateDefault=14)", () => {
                var s = _pi.GetIpcSubscriber<string, int, uint, ulong, int>(ApplyStateLabel);
                return (state, idx) => s.InvokeFunc(state, idx, 0u, ApplyFlagsStateDefault);
            }),
            ("<string, int, uint, int, int>(s, idx, 0, 14)", () => {
                var s = _pi.GetIpcSubscriber<string, int, uint, int, int>(ApplyStateLabel);
                return (state, idx) => s.InvokeFunc(state, idx, 0u, (int)ApplyFlagsStateDefault);
            }),
            ("<string, int, uint, uint, int>(s, idx, 0, 14)", () => {
                var s = _pi.GetIpcSubscriber<string, int, uint, uint, int>(ApplyStateLabel);
                return (state, idx) => s.InvokeFunc(state, idx, 0u, (uint)ApplyFlagsStateDefault);
            }),
        };
        foreach (var (shape, build) in attempts)
        {
            try
            {
                var invoker = build();
                // We can't dry-run ApplyState safely — it would mutate game state. We'll
                // accept whichever build succeeds at GetIpcSubscriber + parameter binding;
                // a signature mismatch surfaces on first real call instead.
                _log.LogInformation("Glamourer.ApplyState bound with shape {Shape}", shape);
                return invoker;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Glamourer.ApplyState shape {Shape} rejected: {Msg}", shape, ex.Message);
            }
        }
        _log.LogWarning("Glamourer.ApplyState: no signature shape bound");
        return null;
    }

    // ---- Revert -------------------------------------------------------------
    private Action<int>? _revertInvoker;
    public void Revert(int objectIndex)
    {
        _revertInvoker ??= ResolveRevertInvoker();
        if (_revertInvoker is null) return;
        try { _revertInvoker(objectIndex); }
        catch (Exception ex) { _log.LogError("Glamourer.RevertState failed: {Msg}", ex.Message); }
    }

    private Action<int>? ResolveRevertInvoker()
    {
        var attempts = new (string Shape, Func<Action<int>> Build)[]
        {
            ("<int, uint, int>(idx, 0)", () => {
                var s = _pi.GetIpcSubscriber<int, uint, int>(RevertStateLabel);
                return idx => s.InvokeFunc(idx, 0u);
            }),
            ("<int, uint, object>(idx, 0)", () => {
                var s = _pi.GetIpcSubscriber<int, uint, object>(RevertStateLabel);
                return idx => s.InvokeAction(idx, 0u);
            }),
            ("<int, object>(idx)", () => {
                var s = _pi.GetIpcSubscriber<int, object>(RevertStateLabel);
                return idx => s.InvokeAction(idx);
            }),
        };
        foreach (var (shape, build) in attempts)
        {
            try
            {
                var invoker = build();
                _log.LogInformation("Glamourer.RevertState bound with shape {Shape}", shape);
                return invoker;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Glamourer.RevertState shape {Shape} rejected: {Msg}", shape, ex.Message);
            }
        }
        _log.LogWarning("Glamourer.RevertState: no signature shape bound");
        return null;
    }
}
