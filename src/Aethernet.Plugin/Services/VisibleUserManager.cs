using System.Collections.Concurrent;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Tracks which paired players are currently visible to the local actor. Polled each frame on
/// the framework thread; emits Add/Remove events on transitions only.
/// </summary>
public sealed class VisibleUserManager : IDisposable
{
    private readonly IFramework _framework;
    private readonly IObjectTable _objectTable;
    private readonly IClientState _clientState;
    private readonly PairManager _pairs;

    private readonly ConcurrentDictionary<string, VisibleEntry> _visible = new(StringComparer.Ordinal);

    public event Action<VisibleEntry>? PlayerBecameVisible;
    public event Action<string>?       PlayerBecameInvisible;

    public VisibleUserManager(
        IFramework framework, IObjectTable objectTable, IClientState clientState, PairManager pairs)
    {
        _framework = framework; _objectTable = objectTable; _clientState = clientState; _pairs = pairs;
        _framework.Update += OnFrameworkTick;
    }

    private DateTime _lastScan = DateTime.MinValue;
    // Param is named `framework` (not `_`) because using `_` shadows the `out _` discard below.
    private void OnFrameworkTick(IFramework framework)
    {
        // Scan no more than 4x per second — every frame is overkill.
        var now = DateTime.UtcNow;
        if (now - _lastScan < TimeSpan.FromMilliseconds(250)) return;
        _lastScan = now;

        if (!_clientState.IsLoggedIn) return;

        var nowVisible = new Dictionary<string, VisibleEntry>(StringComparer.Ordinal);
        foreach (var obj in _objectTable)
        {
            if (obj is not IPlayerCharacter pc) continue;
            // Identity is name@worldId; we rely on the paired user to have published the same key on connect.
            var ident = $"{pc.Name.TextValue}@{pc.HomeWorld.RowId}";
            // Match against pair-published ident strings.
            foreach (var pair in _pairs.OnlineAndUnpaused)
            {
                if (pair.RemoteIdent != ident) continue;
                nowVisible[pair.Pair.User.UID] = new VisibleEntry(
                    pair.Pair.User.UID, ident, pc.ObjectIndex, pc.Address);
                break;
            }
        }

        foreach (var added in nowVisible.Keys.Except(_visible.Keys).ToList())
        {
            _visible[added] = nowVisible[added];
            try { PlayerBecameVisible?.Invoke(_visible[added]); } catch { }
        }
        foreach (var removed in _visible.Keys.Except(nowVisible.Keys).ToList())
        {
            _visible.TryRemove(removed, out _);
            try { PlayerBecameInvisible?.Invoke(removed); } catch { }
        }
    }

    public bool IsVisible(string uid) => _visible.ContainsKey(uid);
    public VisibleEntry? Get(string uid) => _visible.TryGetValue(uid, out var v) ? v : null;
    public IEnumerable<VisibleEntry> AllVisible => _visible.Values;

    public void Dispose() { _framework.Update -= OnFrameworkTick; }
}

public sealed record VisibleEntry(string Uid, string Ident, int ObjectIndex, nint Address);
