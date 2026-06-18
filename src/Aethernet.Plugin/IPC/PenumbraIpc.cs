using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.IPC;

/// <summary>
/// Bridge to Penumbra's IPC surface. We only call the methods we need; refer to the Penumbra
/// docs for the canonical signatures — they may shift between Penumbra versions, so each call
/// is wrapped and guarded.
/// https://github.com/xivdev/Penumbra/wiki/IPC
/// </summary>
public sealed class PenumbraIpc : IDisposable
{
    // Penumbra moved its mutating IPC methods to `.V5`-suffixed labels in 2024. The
    // unversioned names still exist for backwards compat in some cases but the V5
    // versions are what current Penumbra publishes.
    private const string ApiVersionLabel        = "Penumbra.ApiVersions";
    private const string GetEnabledStateLabel    = "Penumbra.GetEnabledState";
    private const string GetModDirectoryLabel    = "Penumbra.GetModDirectory";
    private const string GetGameObjectResourcesLabel = "Penumbra.GetGameObjectResourcePaths.V5";
    private const string GetPlayerResourcesLabel     = "Penumbra.GetPlayerResourcePaths.V5";
    // Labels verified against Penumbra.Api.dll dump 2026-06-14:
    //   CreateTemporaryCollection is V6 (was V5, also dropped "Named")
    //   RedrawObject is V5 (we were using unversioned)
    //   Rest are V5
    private const string CreateNamedTemporaryCollectionLabel = "Penumbra.CreateTemporaryCollection.V6";
    private const string AssignTemporaryCollectionLabel      = "Penumbra.AssignTemporaryCollection.V5";
    private const string AddTemporaryModLabel                = "Penumbra.AddTemporaryMod.V5";
    private const string RemoveTemporaryModLabel             = "Penumbra.RemoveTemporaryMod.V5";
    private const string DeleteTemporaryCollectionLabel      = "Penumbra.DeleteTemporaryCollection.V5";
    private const string GetMetaManipulationsLabel           = "Penumbra.GetMetaManipulations.V5";
    private const string RedrawObjectLabel                   = "Penumbra.RedrawObject.V5";
    private const string GameObjectRedrawnEventLabel         = "Penumbra.GameObjectRedrawn";
    private const string ModSettingChangedEventLabel         = "Penumbra.ModSettingChanged.V5";

    private readonly IDalamudPluginInterface _pi;
    private readonly ILogger<PenumbraIpc>  _log;

    /// <summary>Fires when Penumbra redraws any game object — receivers should re-collect.</summary>
    public event Action<int>? GameObjectRedrawn;
    /// <summary>Fires when the user toggles a mod or its settings.</summary>
    public event Action? ModSettingChanged;

    private readonly List<IDisposable> _eventSubscriptions = new();

    public PenumbraIpc(IDalamudPluginInterface pi, ILogger<PenumbraIpc> log)
    {
        _pi = pi;
        _log = log;
        TrySubscribeToEvents();
    }

    private void TrySubscribeToEvents()
    {
        // GameObjectRedrawn: error "2 != 1" — needs 2 generic types. Likely (nint address, int objIdx).
        try
        {
            var redrawSub = _pi.GetIpcSubscriber<nint, int, object>(GameObjectRedrawnEventLabel);
            Action<nint, int> onRedraw = (_, idx) =>
            {
                _log.LogInformation("Penumbra fired GameObjectRedrawn for objIdx={Idx}", idx);
                try { GameObjectRedrawn?.Invoke(idx); } catch { /* swallow */ }
            };
            redrawSub.Subscribe(onRedraw);
            _eventSubscriptions.Add(new EventUnsubscriber(() => redrawSub.Unsubscribe(onRedraw)));
            _log.LogInformation("Subscribed to Penumbra.GameObjectRedrawn (<nint, int>)");
        }
        catch (Exception ex) { _log.LogWarning("Penumbra.GameObjectRedrawn subscribe failed: {Msg}", ex.Message); }

        // ModSettingChanged: error "4 != 3" — needs 4 generic types. Likely (ModSettingChange type, Guid coll, string modDir, bool inherited).
        try
        {
            var modSub = _pi.GetIpcSubscriber<int, Guid, string, bool, object>(ModSettingChangedEventLabel);
            Action<int, Guid, string, bool> onModChanged = (_, _, _, _) => { try { ModSettingChanged?.Invoke(); } catch { /* swallow */ } };
            modSub.Subscribe(onModChanged);
            _eventSubscriptions.Add(new EventUnsubscriber(() => modSub.Unsubscribe(onModChanged)));
            _log.LogInformation("Subscribed to Penumbra.ModSettingChanged (<int, Guid, string, bool>)");
        }
        catch (Exception ex) { _log.LogWarning("Penumbra.ModSettingChanged subscribe failed: {Msg}", ex.Message); }
    }

    private sealed class EventUnsubscriber : IDisposable
    {
        private readonly Action _dispose;
        public EventUnsubscriber(Action dispose) { _dispose = dispose; }
        public void Dispose() { try { _dispose(); } catch { /* swallow */ } }
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                var ver = _pi.GetIpcSubscriber<(int Breaking, int Feature)>(ApiVersionLabel).InvokeFunc();
                return ver.Breaking > 0;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Penumbra IPC unavailable");
                return false;
            }
        }
    }

    public bool IsEnabled
    {
        get
        {
            try { return _pi.GetIpcSubscriber<bool>(GetEnabledStateLabel).InvokeFunc(); }
            catch { return false; }
        }
    }

    public string? ModDirectory
    {
        get
        {
            try { return _pi.GetIpcSubscriber<string>(GetModDirectoryLabel).InvokeFunc(); }
            catch { return null; }
        }
    }

    private const string GetCollectionForObjectLabel = "Penumbra.GetCollectionForObject.V5";
    private const string GetModListLabel             = "Penumbra.GetModList";
    private const string GetCurrentModSettingsLabel  = "Penumbra.GetCurrentModSettings.V5";

    /// <summary>
    /// Returns the (collectionId, displayName) of the collection currently assigned to the
    /// given game object. Wraps the V5 tuple-return shape: (valid, individual, (Guid, name)).
    /// </summary>
    public (Guid CollectionId, string DisplayName)? GetCollectionForObject(int objectIndex)
    {
        try
        {
            var s = _pi.GetIpcSubscriber<int, (bool valid, bool individual, (Guid id, string name))>(GetCollectionForObjectLabel);
            var (valid, _, coll) = s.InvokeFunc(objectIndex);
            if (!valid || coll.id == Guid.Empty) return null;
            return (coll.id, coll.name);
        }
        catch (Exception ex) { _log.LogWarning("GetCollectionForObject failed: {Msg}", ex.Message); return null; }
    }

    /// <summary>
    /// Returns all known mods as (modDirectory -> modName). modDirectory is the relative folder
    /// under Penumbra's root (combine with <see cref="ModDirectory"/> to get the on-disk path).
    /// </summary>
    public IReadOnlyDictionary<string, string> GetModList()
    {
        try { return _pi.GetIpcSubscriber<Dictionary<string, string>>(GetModListLabel).InvokeFunc(); }
        catch (Exception ex) { _log.LogWarning("GetModList failed: {Msg}", ex.Message); return new Dictionary<string, string>(); }
    }

    /// <summary>
    /// Returns whether the given mod is enabled (effective) in the given collection.
    /// Return tuple shape per IPC: (PenumbraApiEc, (enabled, priority, settings, inheritance)?).
    /// We only care about the boolean enabled flag.
    /// </summary>
    public bool IsModEnabled(Guid collectionId, string modDirectory, string modName)
    {
        try
        {
            var s = _pi.GetIpcSubscriber<Guid, string, string, bool,
                (int status, (bool enabled, int priority, Dictionary<string, List<string>> settings, bool inherit)?)>(
                GetCurrentModSettingsLabel);
            var (status, payload) = s.InvokeFunc(collectionId, modDirectory, modName, false);
            if (status != 0 || payload is null) return false;
            return payload.Value.enabled;
        }
        catch (Exception ex) { _log.LogDebug("IsModEnabled failed for {Mod}: {Msg}", modDirectory, ex.Message); return false; }
    }

    /// <summary>
    /// Enumerates every file under every mod enabled in the local player's effective collection.
    /// This is the supplement to GetResourcePaths: animations (.pap), sounds (.scd), VFX (.avfx)
    /// only show up in resource enumeration when actively playing, so we have to walk the mod
    /// directories to capture them ahead of time. Returns (gameAssumedPath, actualFilePath) pairs
    /// where gameAssumedPath is derived from the file's location relative to the mod directory.
    /// </summary>
    public IEnumerable<(string GamePath, string ActualPath)> EnumerateActiveModFiles(int objectIndex)
    {
        var coll = GetCollectionForObject(objectIndex);
        if (coll is null) { _log.LogInformation("EnumerateActiveModFiles: no collection for idx={Idx}", objectIndex); yield break; }
        var modRoot = ModDirectory;
        if (string.IsNullOrEmpty(modRoot) || !Directory.Exists(modRoot))
        {
            _log.LogWarning("EnumerateActiveModFiles: Penumbra mod root not found ({Root})", modRoot);
            yield break;
        }
        var mods = GetModList();
        _log.LogInformation("EnumerateActiveModFiles: collection={Coll}, checking {Total} mods", coll.Value.DisplayName, mods.Count);

        int enabledCount = 0, fileCount = 0;
        foreach (var (modDir, modName) in mods)
        {
            if (!IsModEnabled(coll.Value.CollectionId, modDir, modName)) continue;
            enabledCount++;
            var fullDir = Path.Combine(modRoot, modDir);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var file in Directory.EnumerateFiles(fullDir, "*", SearchOption.AllDirectories))
            {
                // Skip non-game files (meta.json, default_mod.json, group_*.json, README.md, etc.)
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".json" or ".md" or ".txt" or ".pmp" or ".ttmp" or ".ttmp2") continue;

                // Penumbra mods nest game paths arbitrarily deep inside option-group folders.
                // E.g. "D:\Mod\WUFFY Tails\all files - required\all files [required]\chara\human\..."
                // The actual game-path portion starts at the FIRST recognized root segment (chara,
                // common, etc.) anywhere in the path — not at the immediate relative root.
                var unixFull = file.Replace('\\', '/');
                var gamePath = ExtractGamePath(unixFull);
                if (gamePath is null) continue;

                fileCount++;
                yield return (gamePath, file);
            }
        }
        _log.LogInformation("EnumerateActiveModFiles: {Enabled} enabled mods, {Files} game files yielded", enabledCount, fileCount);
    }

    // FFXIV's top-level data directories. A Penumbra mod file's "game path" is the substring
    // starting at one of these segments and continuing to the file. The mod folder hierarchy
    // above this point is purely organizational (option groups, "all required" wrappers, etc.).
    private static readonly string[] GamePathRoots =
        { "chara", "common", "bg", "bgcommon", "bgparts", "music", "shader", "sound", "ui", "vfx", "exd" };

    private static string? ExtractGamePath(string forwardSlashedFullPath)
    {
        // Look for the LAST occurrence of "/{root}/" in the path — last because earlier matches
        // could be folder names that incidentally collide with root names.
        for (int i = forwardSlashedFullPath.Length - 1; i >= 0; i--)
        {
            if (forwardSlashedFullPath[i] != '/') continue;
            foreach (var root in GamePathRoots)
            {
                // Need: "/{root}/" at this slash position
                if (i + 1 + root.Length >= forwardSlashedFullPath.Length) continue;
                if (forwardSlashedFullPath[i + 1 + root.Length] != '/') continue;
                if (string.CompareOrdinal(forwardSlashedFullPath, i + 1, root, 0, root.Length) != 0) continue;
                return forwardSlashedFullPath[(i + 1)..];
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the file-replacements applied to a given game object: in-game path -> actual file path.
    /// Penumbra .V5's exact shape isn't documented — we probe four candidate signatures on first
    /// call, log which one returns non-empty data, and cache the winner.
    /// </summary>
    private Func<int, IReadOnlyDictionary<string, string>>? _resourcePathsInvoker;
    public IReadOnlyDictionary<string, string> GetResourcePaths(int objectIndex)
    {
        // For the local player (idx 0), prefer GetPlayerResourcePaths.V5 — it returns
        // post-mod-resolution paths (actual modded files), unlike GetGameObjectResourcePaths
        // which may report raw game paths.
        if (objectIndex == 0)
        {
            var playerPaths = TryGetPlayerResourcePaths();
            // Fall through to GameObject API when Player is null or empty — the histogram
            // diagnostic in CollectForObject needs the full GameObject list to identify which
            // race-coded paths the character is actually loading.
            if (playerPaths is { Count: > 0 }) return playerPaths;
        }

        _resourcePathsInvoker ??= ResolveResourcePathsInvoker(objectIndex);
        if (_resourcePathsInvoker is null) return new Dictionary<string, string>();
        try { return _resourcePathsInvoker(objectIndex); }
        catch (Exception ex)
        {
            _log.LogWarning("GetResourcePaths failed for objectIndex={Idx}: {Msg}", objectIndex, ex.Message);
            return new Dictionary<string, string>();
        }
    }

    private bool _playerPathsUnavailable;
    private Func<IReadOnlyDictionary<string, string>>? _playerPathsInvoker;

    private IReadOnlyDictionary<string, string>? TryGetPlayerResourcePaths()
    {
        if (_playerPathsUnavailable) return null;
        _playerPathsInvoker ??= ResolvePlayerPathsInvoker();
        if (_playerPathsInvoker is null) { _playerPathsUnavailable = true; return null; }
        try { return _playerPathsInvoker(); }
        catch (Exception ex)
        {
            _log.LogWarning("GetPlayerResourcePaths.V5 call failed: {Msg}", ex.Message);
            return null;
        }
    }

    private Func<IReadOnlyDictionary<string, string>>? ResolvePlayerPathsInvoker()
    {
        // Penumbra's GetPlayerResourcePaths.V5 returns Dictionary<,> but we have to find
        // the exact value type. Try common shapes; first success wins.
        var attempts = new (string Shape, Func<Func<IReadOnlyDictionary<string, string>>> Build)[]
        {
            ("Dictionary<string, HashSet<string>>", () => {
                var s = _pi.GetIpcSubscriber<Dictionary<string, HashSet<string>>>(GetPlayerResourcesLabel);
                return () => FlattenHashSetDict(s.InvokeFunc());
            }),
            ("Dictionary<string, string[]>", () => {
                var s = _pi.GetIpcSubscriber<Dictionary<string, string[]>>(GetPlayerResourcesLabel);
                return () => FlattenStringArrayDict(s.InvokeFunc());
            }),
            // Maybe key is something other than string:
            ("Dictionary<int, HashSet<string>>", () => {
                var s = _pi.GetIpcSubscriber<Dictionary<int, HashSet<string>>>(GetPlayerResourcesLabel);
                return () => {
                    var raw = s.InvokeFunc();
                    if (raw is null) return new Dictionary<string, string>();
                    var flat = new Dictionary<string, string>(raw.Count);
                    foreach (var (k, vs) in raw)
                    {
                        var first = vs.FirstOrDefault();
                        if (!string.IsNullOrEmpty(first)) flat[k.ToString()] = first;
                    }
                    return flat;
                };
            }),
            // Maybe player API returns an array of dicts like GameObject does:
            ("Dictionary<string, HashSet<string>>?[]", () => {
                var s = _pi.GetIpcSubscriber<Dictionary<string, HashSet<string>>?[]>(GetPlayerResourcesLabel);
                return () => {
                    var arr = s.InvokeFunc();
                    return FlattenHashSetDict(arr is { Length: > 0 } ? arr[0] : null);
                };
            }),
            // Or arrays with string[]:
            ("Dictionary<string, string[]>?[]", () => {
                var s = _pi.GetIpcSubscriber<Dictionary<string, string[]>?[]>(GetPlayerResourcesLabel);
                return () => {
                    var arr = s.InvokeFunc();
                    return FlattenStringArrayDict(arr is { Length: > 0 } ? arr[0] : null);
                };
            }),
            // Maybe the value is a tuple — Penumbra sometimes wraps:
            ("Dictionary<string, (string, string)>", () => {
                var s = _pi.GetIpcSubscriber<Dictionary<string, (string, string)>>(GetPlayerResourcesLabel);
                return () => {
                    var raw = s.InvokeFunc();
                    if (raw is null) return new Dictionary<string, string>();
                    var flat = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var (k, tup) in raw)
                        if (!string.IsNullOrEmpty(tup.Item1)) flat[k] = tup.Item1;
                    return flat;
                };
            }),
            // Maybe the entire thing is wrapped in an outer tuple (like Glamourer's status pattern):
            ("(int, Dictionary<string, HashSet<string>>)", () => {
                var s = _pi.GetIpcSubscriber<(int, Dictionary<string, HashSet<string>>)>(GetPlayerResourcesLabel);
                return () => FlattenHashSetDict(s.InvokeFunc().Item2);
            }),
        };

        foreach (var (shape, build) in attempts)
        {
            try
            {
                var invoker = build();
                var test = invoker();  // probe-call to confirm shape
                _log.LogInformation("GetPlayerResourcePaths.V5 bound with {Shape}, probe returned {N} entries", shape, test.Count);
                return invoker;
            }
            catch (Exception ex)
            {
                _log.LogWarning("GetPlayerResourcePaths.V5 shape {Shape} rejected: {Msg}", shape, ex.Message);
            }
        }
        _log.LogWarning("GetPlayerResourcePaths.V5: no signature shape bound");
        return null;
    }

    // Penumbra V5's dictionary semantics: KEY = actual on-disk file path, VALUE = set of
    // game paths that file satisfies. So one mod file can replace many game paths (e.g. a
    // shared shader). We expand the hashset and produce gamePath → modFile mapping so the
    // caller can iterate as "for each game path the character needs, here's the actual file".
    private static IReadOnlyDictionary<string, string> FlattenHashSetDict(Dictionary<string, HashSet<string>>? raw)
    {
        if (raw is null) return new Dictionary<string, string>();
        var flat = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (actualFile, gamePaths) in raw)
        {
            foreach (var gamePath in gamePaths)
            {
                if (!string.IsNullOrEmpty(gamePath)) flat[gamePath] = actualFile;
            }
        }
        return flat;
    }
    private static IReadOnlyDictionary<string, string> FlattenStringArrayDict(Dictionary<string, string[]>? raw)
    {
        if (raw is null) return new Dictionary<string, string>();
        var flat = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (actualFile, gamePaths) in raw)
        {
            foreach (var gamePath in gamePaths)
            {
                if (!string.IsNullOrEmpty(gamePath)) flat[gamePath] = actualFile;
            }
        }
        return flat;
    }

    private Func<int, IReadOnlyDictionary<string, string>>? ResolveResourcePathsInvoker(int sampleIdx)
    {
        // Penumbra V5 dict semantics: KEY = actual on-disk file, VALUE = set of game paths
        // that file satisfies. We expand the set so each game path maps to its (single)
        // satisfying on-disk file — that's the shape CharacterDataCollector iterates.
        static IReadOnlyDictionary<string, string> FlattenHashSet(Dictionary<string, HashSet<string>>? raw)
        {
            if (raw is null) return new Dictionary<string, string>();
            var flat = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (actualFile, gamePaths) in raw)
                foreach (var gamePath in gamePaths)
                    if (!string.IsNullOrEmpty(gamePath)) flat[gamePath] = actualFile;
            return flat;
        }
        static IReadOnlyDictionary<string, string> FlattenStringArray(Dictionary<string, string[]>? raw)
        {
            if (raw is null) return new Dictionary<string, string>();
            var flat = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (actualFile, gamePaths) in raw)
                foreach (var gamePath in gamePaths)
                    if (!string.IsNullOrEmpty(gamePath)) flat[gamePath] = actualFile;
            return flat;
        }

        var attempts = new (string Shape, Func<Func<int, IReadOnlyDictionary<string, string>>> Build)[]
        {
            ("<ushort[], Dictionary<string, HashSet<string>>?[]>", () => {
                var s = _pi.GetIpcSubscriber<ushort[], Dictionary<string, HashSet<string>>?[]>(GetGameObjectResourcesLabel);
                return idx => {
                    var r = s.InvokeFunc(new[] { (ushort)idx });
                    return FlattenHashSet(r is { Length: > 0 } ? r[0] : null);
                };
            }),
            ("<ushort[], Dictionary<string, string[]>?[]>", () => {
                var s = _pi.GetIpcSubscriber<ushort[], Dictionary<string, string[]>?[]>(GetGameObjectResourcesLabel);
                return idx => {
                    var r = s.InvokeFunc(new[] { (ushort)idx });
                    return FlattenStringArray(r is { Length: > 0 } ? r[0] : null);
                };
            }),
            ("<int[], Dictionary<string, HashSet<string>>?[]>", () => {
                var s = _pi.GetIpcSubscriber<int[], Dictionary<string, HashSet<string>>?[]>(GetGameObjectResourcesLabel);
                return idx => {
                    var r = s.InvokeFunc(new[] { idx });
                    return FlattenHashSet(r is { Length: > 0 } ? r[0] : null);
                };
            }),
            ("<ushort, Dictionary<string, HashSet<string>>?>", () => {
                var s = _pi.GetIpcSubscriber<ushort, Dictionary<string, HashSet<string>>?>(GetGameObjectResourcesLabel);
                return idx => FlattenHashSet(s.InvokeFunc((ushort)idx));
            }),
            ("<int, Dictionary<string, HashSet<string>>?>", () => {
                var s = _pi.GetIpcSubscriber<int, Dictionary<string, HashSet<string>>?>(GetGameObjectResourcesLabel);
                return idx => FlattenHashSet(s.InvokeFunc(idx));
            }),
        };

        foreach (var (shape, build) in attempts)
        {
            try
            {
                var invoker = build();
                // Test-call against the sample index. Empty result is OK at this stage — we lock
                // in the first signature that doesn't throw, since shapes that throw are wrong.
                var probeResult = invoker(sampleIdx);
                _log.LogInformation("Penumbra.GetGameObjectResourcePaths bound with {Shape} (probe returned {N} entries)",
                    shape, probeResult.Count);
                return invoker;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Penumbra shape {Shape} rejected: {Msg}", shape, ex.Message);
            }
        }
        _log.LogWarning("Penumbra.GetGameObjectResourcePaths: no signature shape bound");
        return null;
    }

    public string GetMetaManipulations(int objectIndex)
    {
        try { return _pi.GetIpcSubscriber<int, string>(GetMetaManipulationsLabel).InvokeFunc(objectIndex) ?? string.Empty; }
        catch (Exception ex) { _log.LogWarning("GetMetaManipulations failed: {Msg}", ex.Message); return string.Empty; }
    }

    private Func<string, Guid>? _createCollectionInvoker;
    public Guid CreateTemporaryCollection(string name)
    {
        _createCollectionInvoker ??= ResolveCreateCollectionInvoker();
        if (_createCollectionInvoker is null) return Guid.Empty;
        try { return _createCollectionInvoker(name); }
        catch (Exception ex) { _log.LogError("CreateTemporaryCollection invoke failed: {Msg}", ex.Message); return Guid.Empty; }
    }

    private Func<string, Guid>? ResolveCreateCollectionInvoker()
    {
        // V6 takes 2 inputs and returns ValueTuple<,>. Probably (status, Guid) à la Glamourer.
        // Tries both (status, Guid) and (Guid, status) orderings across likely input shapes.
        var attempts = new (string Shape, Func<Func<string, Guid>> Build)[]
        {
            ("<string, string, (int, Guid)>", () => {
                var s = _pi.GetIpcSubscriber<string, string, (int, Guid)>(CreateNamedTemporaryCollectionLabel);
                return name => s.InvokeFunc(name, name).Item2;
            }),
            ("<Guid, string, (int, Guid)>", () => {
                var s = _pi.GetIpcSubscriber<Guid, string, (int, Guid)>(CreateNamedTemporaryCollectionLabel);
                return name => s.InvokeFunc(Guid.NewGuid(), name).Item2;
            }),
            ("<string, string, (Guid, int)>", () => {
                var s = _pi.GetIpcSubscriber<string, string, (Guid, int)>(CreateNamedTemporaryCollectionLabel);
                return name => s.InvokeFunc(name, name).Item1;
            }),
            ("<string, bool, (int, Guid)>", () => {
                var s = _pi.GetIpcSubscriber<string, bool, (int, Guid)>(CreateNamedTemporaryCollectionLabel);
                return name => s.InvokeFunc(name, true).Item2;
            }),
            ("<string, int, (int, Guid)>", () => {
                var s = _pi.GetIpcSubscriber<string, int, (int, Guid)>(CreateNamedTemporaryCollectionLabel);
                return name => s.InvokeFunc(name, 0).Item2;
            }),
            ("<string, string, (byte, Guid)>", () => {
                var s = _pi.GetIpcSubscriber<string, string, (byte, Guid)>(CreateNamedTemporaryCollectionLabel);
                return name => s.InvokeFunc(name, name).Item2;
            }),
        };
        foreach (var (shape, build) in attempts)
        {
            try
            {
                var invoker = build();
                // Probe with a sentinel name; if the call works we know the signature.
                var probe = invoker("aethernet-probe");
                _log.LogInformation("CreateTemporaryCollection bound with {Shape}, probe returned {Guid}", shape, probe);
                // Clean up the probe collection so we don't leak it.
                if (probe != Guid.Empty)
                {
                    try { DeleteTemporaryCollection(probe); } catch { /* swallow */ }
                }
                return invoker;
            }
            catch (Exception ex)
            {
                _log.LogWarning("CreateTemporaryCollection shape {Shape} rejected: {Msg}", shape, ex.Message);
            }
        }
        _log.LogError("CreateTemporaryCollection: no signature shape bound");
        return null;
    }

    // ---- AssignTemporaryCollection ------------------------------------------
    // Dalamud's GetIpcSubscriber<...>() does NOT validate the generic count against the
    // server-registered signature — that only happens at invoke time. We try-invoke each
    // candidate; whichever doesn't throw wins, then we cache the invoker. Func variants
    // also return a status code (PenumbraApiEc) — non-zero means failure, we log it.
    private Func<Guid, int, int>? _assignInvoker;
    public void AssignTemporaryCollection(Guid collection, int objectIndex)
    {
        if (_assignInvoker is not null)
        {
            try
            {
                var status = _assignInvoker(collection, objectIndex);
                if (status != 0) _log.LogWarning("AssignTemporaryCollection status={Status} (collection={Coll}, idx={Idx})", status, collection, objectIndex);
                else             _log.LogInformation("AssignTemporaryCollection OK collection={Coll}, idx={Idx}", collection, objectIndex);
            }
            catch (Exception ex) { _log.LogError("AssignTemporaryCollection cached invoke: {Msg}", ex.Message); }
            return;
        }
        // Server has 3 inputs. Try each plausible 3-input shape. Returns int status (0 == success).
        var attempts = new (string Shape, Func<Guid, int, int> Invoke)[]
        {
            ("<Guid, int, bool, int> Func(c, idx, true)", (c, i) =>
                _pi.GetIpcSubscriber<Guid, int, bool, int>(AssignTemporaryCollectionLabel).InvokeFunc(c, i, true)),
            ("<Guid, int, byte, int> Func(c, idx, 1)", (c, i) =>
                _pi.GetIpcSubscriber<Guid, int, byte, int>(AssignTemporaryCollectionLabel).InvokeFunc(c, i, (byte)1)),
            ("<Guid, int, int, int> Func(c, idx, 0)", (c, i) =>
                _pi.GetIpcSubscriber<Guid, int, int, int>(AssignTemporaryCollectionLabel).InvokeFunc(c, i, 0)),
            ("<Guid, int, bool, object> Action(c, idx, true)", (c, i) =>
                { _pi.GetIpcSubscriber<Guid, int, bool, object>(AssignTemporaryCollectionLabel).InvokeAction(c, i, true); return 0; }),
            ("<Guid, int, int, object> Action(c, idx, 0)", (c, i) =>
                { _pi.GetIpcSubscriber<Guid, int, int, object>(AssignTemporaryCollectionLabel).InvokeAction(c, i, 0); return 0; }),
        };
        foreach (var (shape, invoke) in attempts)
        {
            try
            {
                var status = invoke(collection, objectIndex);
                _log.LogInformation("AssignTemporaryCollection bound with {Shape}, status={Status}", shape, status);
                _assignInvoker = invoke;
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning("Assign shape {Shape} rejected: {Msg}", shape, ex.Message);
            }
        }
        _log.LogError("AssignTemporaryCollection: no signature shape worked");
    }

    // ---- AddTemporaryMod ----------------------------------------------------
    private Func<string, Guid, Dictionary<string, string>, string, int, int>? _addModInvoker;
    public void AddTemporaryMod(string tag, Guid collection,
        Dictionary<string, string> fileReplacements, string manipulationData, int priority)
    {
        _addModInvoker ??= ResolveAddModInvoker();
        if (_addModInvoker is null) return;
        try
        {
            var status = _addModInvoker(tag, collection, fileReplacements, manipulationData, priority);
            if (status != 0) _log.LogWarning("AddTemporaryMod status={Status} tag={Tag} fileCount={Cnt}", status, tag, fileReplacements.Count);
            else             _log.LogInformation("AddTemporaryMod OK tag={Tag} fileCount={Cnt}", tag, fileReplacements.Count);
        }
        catch (Exception ex) { _log.LogError("AddTemporaryMod invoke: {Msg}", ex.Message); }
    }
    private Func<string, Guid, Dictionary<string, string>, string, int, int>? ResolveAddModInvoker()
    {
        var attempts = new (string Shape, Func<Func<string, Guid, Dictionary<string, string>, string, int, int>> Build)[]
        {
            ("<string, Guid, Dict, string, int, int> Func<int>", () => {
                var s = _pi.GetIpcSubscriber<string, Guid, Dictionary<string, string>, string, int, int>(AddTemporaryModLabel);
                return (tag, c, files, meta, prio) => s.InvokeFunc(tag, c, files, meta, prio);
            }),
            ("<Guid, string, Dict, string, int, int>(coll, tag, ...) reordered", () => {
                var s = _pi.GetIpcSubscriber<Guid, string, Dictionary<string, string>, string, int, int>(AddTemporaryModLabel);
                return (tag, c, files, meta, prio) => s.InvokeFunc(c, tag, files, meta, prio);
            }),
        };
        foreach (var (shape, build) in attempts)
        {
            try { var inv = build(); _log.LogInformation("AddTemporaryMod bound with {Shape}", shape); return inv; }
            catch (Exception ex) { _log.LogWarning("AddMod shape {Shape} rejected: {Msg}", shape, ex.Message); }
        }
        _log.LogError("AddTemporaryMod: no signature shape bound"); return null;
    }

    // ---- RemoveTemporaryMod -------------------------------------------------
    public void RemoveTemporaryMod(string tag, Guid collection, int priority)
    {
        try { _pi.GetIpcSubscriber<string, Guid, int, int>(RemoveTemporaryModLabel)
                   .InvokeFunc(tag, collection, priority); }
        catch (Exception ex) { _log.LogWarning("RemoveTemporaryMod: {Msg}", ex.Message); }
    }

    // ---- DeleteTemporaryCollection ------------------------------------------
    private Action<Guid>? _deleteInvoker;
    public void DeleteTemporaryCollection(Guid collection)
    {
        _deleteInvoker ??= ResolveDeleteInvoker();
        if (_deleteInvoker is null) return;
        try { _deleteInvoker(collection); }
        catch (Exception ex) { _log.LogError("DeleteTemporaryCollection invoke: {Msg}", ex.Message); }
    }
    private Action<Guid>? ResolveDeleteInvoker()
    {
        var attempts = new (string Shape, Func<Action<Guid>> Build)[]
        {
            ("<Guid, int> Func<int>", () => {
                var s = _pi.GetIpcSubscriber<Guid, int>(DeleteTemporaryCollectionLabel);
                return c => s.InvokeFunc(c);
            }),
            ("<Guid, object> Action", () => {
                var s = _pi.GetIpcSubscriber<Guid, object>(DeleteTemporaryCollectionLabel);
                return c => s.InvokeAction(c);
            }),
        };
        foreach (var (shape, build) in attempts)
        {
            try { var inv = build(); _log.LogInformation("DeleteTemporaryCollection bound with {Shape}", shape); return inv; }
            catch (Exception ex) { _log.LogWarning("Delete shape {Shape} rejected: {Msg}", shape, ex.Message); }
        }
        _log.LogError("DeleteTemporaryCollection: no signature shape bound"); return null;
    }

    // ---- RedrawObject -------------------------------------------------------
    private Action<int>? _redrawInvoker;
    public void RedrawObject(int objectIndex)
    {
        _redrawInvoker ??= ResolveRedrawInvoker();
        if (_redrawInvoker is null) return;
        try
        {
            _log.LogInformation("RedrawObject(idx={Idx}) calling Penumbra", objectIndex);
            _redrawInvoker(objectIndex);
        }
        catch (Exception ex) { _log.LogError("RedrawObject invoke: {Msg}", ex.Message); }
    }
    private Action<int>? ResolveRedrawInvoker()
    {
        var attempts = new (string Shape, Func<Action<int>> Build)[]
        {
            ("<int, int, object>(idx, 0) Action", () => {
                var s = _pi.GetIpcSubscriber<int, int, object>(RedrawObjectLabel);
                return idx => s.InvokeAction(idx, 0);
            }),
            ("<int, int, int>(idx, 0) Func<int>", () => {
                var s = _pi.GetIpcSubscriber<int, int, int>(RedrawObjectLabel);
                return idx => s.InvokeFunc(idx, 0);
            }),
            ("<int, byte, object>(idx, 0)", () => {
                var s = _pi.GetIpcSubscriber<int, byte, object>(RedrawObjectLabel);
                return idx => s.InvokeAction(idx, (byte)0);
            }),
        };
        foreach (var (shape, build) in attempts)
        {
            try { var inv = build(); _log.LogInformation("RedrawObject bound with {Shape}", shape); return inv; }
            catch (Exception ex) { _log.LogWarning("Redraw shape {Shape} rejected: {Msg}", shape, ex.Message); }
        }
        _log.LogError("RedrawObject: no signature shape bound"); return null;
    }

    public void Dispose()
    {
        foreach (var sub in _eventSubscriptions) sub.Dispose();
        _eventSubscriptions.Clear();
    }
}
