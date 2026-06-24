using System.Collections.Concurrent;
using Aethernet.API.Dto;
using Aethernet.Plugin.IPC;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using AethernetObjectKind = Aethernet.API.Dto.ObjectKind;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Applies incoming character data envelopes to other players' game objects. Manages one
/// Penumbra temporary collection per paired UID, applies per-object kinds, and tears down
/// when the player leaves visibility.
/// </summary>
public sealed class CharacterDataApplier
{
    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly CustomizePlusIpc _customize;
    private readonly HonorificIpc _honorific;
    private readonly HeelsIpc _heels;
    private readonly MoodlesIpc _moodles;
    private readonly PetNamesIpc _petnames;
    private readonly BrioIpc _brio;
    private readonly FileCacheService _cache;
    private readonly FileTransferService _transfer;
    private readonly VisibleUserManager _visible;
    private readonly PairManager _pairs;
    private readonly IObjectTable _objectTable;
    private readonly IFramework _framework;
    private readonly ILogger<CharacterDataApplier> _log;

    private readonly ConcurrentDictionary<string, Guid> _collections = new();
    private readonly ConcurrentDictionary<string, long> _lastApplied  = new();

    public CharacterDataApplier(
        PenumbraIpc penumbra, GlamourerIpc glamourer, CustomizePlusIpc customize,
        HonorificIpc honorific, HeelsIpc heels, MoodlesIpc moodles, PetNamesIpc petnames, BrioIpc brio,
        FileCacheService cache, FileTransferService transfer,
        VisibleUserManager visible, PairManager pairs,
        IObjectTable objectTable, IFramework framework, ILogger<CharacterDataApplier> log)
    {
        _penumbra = penumbra; _glamourer = glamourer; _customize = customize;
        _honorific = honorific; _heels = heels; _moodles = moodles; _petnames = petnames; _brio = brio;
        _cache = cache; _transfer = transfer;
        _visible = visible; _pairs = pairs; _objectTable = objectTable;
        _framework = framework; _log = log;

        _visible.PlayerBecameInvisible += OnInvisible;
    }

    public async Task HandleAsync(OnlineUserCharaDataMessageDto msg, CancellationToken ct)
    {
        var uid  = msg.User.UID;
        var data = msg.CharacterData;

        if (_lastApplied.TryGetValue(uid, out var lastVer) && lastVer >= data.DataVersion) return;
        if (!_pairs.TryGet(uid, out var pair)) { _log.LogDebug("ignoring data from unpaired {Uid}", uid); return; }

        data = ApplyLocalPermissions(data, pair.Pair.OwnPermissions);

        var visible = _visible.Get(uid);
        if (visible is null) { _log.LogInformation("deferring apply for {Uid} until visible (no match in object table)", uid); return; }
        _log.LogInformation("Applying character data from {Uid} (ident={Ident} objIdx={Idx}, {FileCount} file replacements)",
            uid, visible.Ident, visible.ObjectIndex,
            data.Appearances.Values.Sum(a => a.FileReplacements.Count));

        var allHashes = data.Appearances.Values
            .SelectMany(a => a.FileReplacements.Values)
            .Distinct().ToList();
        _log.LogInformation("Downloading {Count} unique files for {Uid}", allHashes.Count, uid);
        await _transfer.DownloadManyAsync(allHashes, ownerUid: uid, ct);

        // Verify cache state after download.
        var cached = allHashes.Count(h => File.Exists(_cache.GetPath(h)));
        var missing = allHashes.Count - cached;
        _log.LogInformation("Cache check: {Cached}/{Total} files present on disk ({Missing} missing)",
            cached, allHashes.Count, missing);
        if (missing > 0)
        {
            foreach (var h in allHashes.Where(h => !File.Exists(_cache.GetPath(h))).Take(3))
                _log.LogWarning("  missing hash {Hash} -> {Path}", h, _cache.GetPath(h));
        }

        await _framework.RunOnTick(() =>
        {
            var collection = _collections.GetOrAdd(uid, _ => _penumbra.CreateTemporaryCollection($"aethernet:{uid}"));
            if (collection == Guid.Empty) { _log.LogWarning("could not create Penumbra collection for {Uid}", uid); return; }
            _log.LogInformation("Using collection {Coll} for {Uid}", collection, uid);

            foreach (var (kind, appearance) in data.Appearances)
            {
                var targetIndex = ResolveActorIndex(visible, kind);
                if (targetIndex < 0) { _log.LogWarning("  kind={Kind} skipped: no target index", kind); continue; }

                // Penumbra requires the replacement file's extension to match the game path's
                // extension. _cache.GetPath(hash, gamePath) hardlinks to an extensioned twin on
                // first use so Penumbra accepts the replacement.
                var mapping = appearance.FileReplacements.ToDictionary(
                    kv => kv.Key,
                    kv => _cache.GetPath(kv.Value, kv.Key),
                    StringComparer.Ordinal);
                foreach (var (src, dst) in appearance.FileSwaps)
                    mapping[src] = dst;

                _log.LogInformation("  kind={Kind} targetIdx={Idx} replacements={Cnt} | sample: {Sample}",
                    kind, targetIndex, mapping.Count,
                    string.Join(" || ", mapping.Take(2).Select(kv => $"{kv.Key} -> {kv.Value}")));

                // Critical order: populate the collection's mods BEFORE assigning it to the
                // actor. Penumbra snapshots the collection state at assignment time, so adding
                // mods after assign means the actor sees an empty collection — no replacements
                // apply, even though AddTemporaryMod returns Success.
                var tag = $"aethernet:{uid}:{kind}";
                _penumbra.AddTemporaryMod(tag, collection, mapping, appearance.ManipulationData ?? "", priority: 0);
                _penumbra.AssignTemporaryCollection(collection, targetIndex);
            }

            if (data.GlamourerData     is not null && _glamourer.IsAvailable) _glamourer.ApplyState(data.GlamourerData, visible.ObjectIndex);
            if (data.CustomizePlusData is not null && _customize.IsAvailable) _customize.ApplyProfile(visible.ObjectIndex, data.CustomizePlusData);
            if (data.HonorificTitle    is not null && _honorific.IsAvailable) _honorific.ApplyTitle(visible.ObjectIndex, data.HonorificTitle);
            if (data.HeelsOffset       is not null && _heels.IsAvailable)     _heels.RegisterOffset(visible.ObjectIndex, data.HeelsOffset);
            if (data.MoodlesData       is not null && _moodles.IsAvailable)   _moodles.ApplyStatus(visible.Address, data.MoodlesData);
            if (data.PetNamesData      is not null && _petnames.IsAvailable)  _petnames.ApplyNames(visible.Ident, data.PetNamesData);
            if (data.BrioData          is not null && _brio.IsAvailable)      _brio.ApplyPose(visible.ObjectIndex, data.BrioData);

            _penumbra.RedrawObject(visible.ObjectIndex);
            foreach (var owned in EnumerateOwnedObjects(visible))
                _penumbra.RedrawObject(owned.ObjectIndex);

            pair.LastAppliedAt = DateTime.UtcNow;
            _lastApplied[uid] = data.DataVersion;
        }, cancellationToken: ct).ConfigureAwait(false);
    }

    private int ResolveActorIndex(VisibleEntry player, AethernetObjectKind kind)
    {
        if (kind == AethernetObjectKind.Player) return player.ObjectIndex;
        var owned = EnumerateOwnedObjects(player)
            .FirstOrDefault(o => MapKind(o.ObjectKind) == kind);
        return owned?.ObjectIndex ?? -1;
    }

    private IEnumerable<IGameObject> EnumerateOwnedObjects(VisibleEntry player)
    {
        var playerObj = _objectTable.SearchById((ulong)player.ObjectIndex);
        if (playerObj is null) yield break;
        foreach (var obj in _objectTable)
        {
            if (obj is null) continue;
            if (obj.OwnerId != playerObj.GameObjectId) continue;
            yield return obj;
        }
    }

    private static AethernetObjectKind? MapKind(Dalamud.Game.ClientState.Objects.Enums.ObjectKind k) => k switch
    {
        Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount     => AethernetObjectKind.Mount,
        Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion => AethernetObjectKind.Minion,
        Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc => AethernetObjectKind.Pet,
        Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc  => AethernetObjectKind.Companion,
        _ => null,
    };

    private void OnInvisible(string uid)
    {
        if (!_collections.TryRemove(uid, out var collection)) return;
        _penumbra.DeleteTemporaryCollection(collection);
        _lastApplied.TryRemove(uid, out _);
    }

    private static CharacterDataDto ApplyLocalPermissions(CharacterDataDto data, UserPermissions perms)
    {
        if (perms == UserPermissions.None) return data;
        if (perms.HasFlag(UserPermissions.DisableHonorific)) data = data with { HonorificTitle    = null };
        if (perms.HasFlag(UserPermissions.DisableHeels))     data = data with { HeelsOffset       = null };
        if (perms.HasFlag(UserPermissions.DisableMoodles))   data = data with { MoodlesData       = null };
        if (perms.HasFlag(UserPermissions.DisablePetNames))  data = data with { PetNamesData      = null };
        if (perms.HasFlag(UserPermissions.DisableCustomize)) data = data with { CustomizePlusData = null };

        if (perms.HasFlag(UserPermissions.DisableAnimations)
            || perms.HasFlag(UserPermissions.DisableSounds)
            || perms.HasFlag(UserPermissions.DisableVfx))
        {
            var filtered = new Dictionary<AethernetObjectKind, ObjectAppearanceDto>(data.Appearances.Count);
            foreach (var (kind, ap) in data.Appearances)
            {
                var fr = ap.FileReplacements.Where(kv =>
                {
                    var ext = Path.GetExtension(kv.Key);
                    if (perms.HasFlag(UserPermissions.DisableAnimations) && ext is ".pap") return false;
                    if (perms.HasFlag(UserPermissions.DisableSounds)     && ext is ".scd") return false;
                    if (perms.HasFlag(UserPermissions.DisableVfx)        && ext is ".avfx") return false;
                    return true;
                }).ToDictionary(kv => kv.Key, kv => kv.Value);
                filtered[kind] = new ObjectAppearanceDto
                {
                    ManipulationData = ap.ManipulationData,
                    FileSwaps        = ap.FileSwaps,
                    FileReplacements = fr,
                };
            }
            data = data with { Appearances = filtered };
        }
        return data;
    }
}
