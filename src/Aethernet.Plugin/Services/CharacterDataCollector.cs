using System.Text;
using System.Text.Json;
using Aethernet.API.Dto;
using Aethernet.Plugin.Configuration;
using Aethernet.Plugin.IPC;
using Aethernet.Shared.Hashing;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;
using AethernetObjectKind = Aethernet.API.Dto.ObjectKind;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Collects the local player's full modded state (including owned actors) into a
/// <see cref="CharacterDataDto"/>. Penumbra resource paths are hashed into the file cache
/// (deduped) so the DTO only carries hashes — bytes are uploaded by the file transfer service.
/// </summary>
public sealed class CharacterDataCollector
{
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly IFramework _framework;
    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly CustomizePlusIpc _customize;
    private readonly HonorificIpc _honorific;
    private readonly HeelsIpc _heels;
    private readonly MoodlesIpc _moodles;
    private readonly PetNamesIpc _petnames;
    private readonly BrioIpc _brio;
    private readonly FileCacheService _cache;
    private readonly AethernetConfig _config;
    private readonly ILogger<CharacterDataCollector> _log;

    private long _version;

    public CharacterDataCollector(
        IClientState clientState, IObjectTable objectTable, IFramework framework,
        PenumbraIpc penumbra, GlamourerIpc glamourer, CustomizePlusIpc customize,
        HonorificIpc honorific, HeelsIpc heels, MoodlesIpc moodles, PetNamesIpc petnames, BrioIpc brio,
        FileCacheService cache, AethernetConfig config,
        ILogger<CharacterDataCollector> log)
    {
        _clientState = clientState; _objectTable = objectTable; _framework = framework;
        _penumbra = penumbra; _glamourer = glamourer; _customize = customize;
        _honorific = honorific; _heels = heels; _moodles = moodles; _petnames = petnames; _brio = brio;
        _cache = cache; _config = config; _log = log;
    }

    public async Task<(CharacterDataDto Data, Dictionary<string, string> HashToPath)> CollectAsync(CancellationToken ct)
    {
        // Phase 1 (on game tick): gather all data that requires the game framework thread.
        // This includes ALL Penumbra/Glamourer/etc. IPC calls — they invoke providers that touch
        // game internals and must run on the tick. Fast: just function returns, no file I/O.
        // We collect game-path -> on-disk-path pairs here but DON'T hash them yet.
        var snapshot = await _framework.RunOnTick(() =>
        {
            var local = _objectTable[0] as IPlayerCharacter;
            if (local is null) return null;

            var perObject = new List<PerObjectSnapshot>();
            perObject.Add(GatherForObject(AethernetObjectKind.Player, local));
            foreach (var (kind, obj) in EnumerateOwnedObjects(local).Select(o => (o.Kind, o.Object)))
                if (!perObject.Any(p => p.Kind == kind))
                    perObject.Add(GatherForObject(kind, obj));

            return new TickSnapshot(
                PlayerIdx:     local.ObjectIndex,
                PlayerAddress: local.Address,
                PerObject:     perObject,
                GlamourerData: _glamourer.IsAvailable ? _glamourer.GetStateBase64(local.ObjectIndex) : null,
                CustomizeData: _customize.IsAvailable ? _customize.GetProfileJson(local.ObjectIndex) : null,
                HonorificData: _honorific.IsAvailable ? _honorific.GetTitleJson(local.ObjectIndex) : null,
                HeelsData:     _heels.IsAvailable     ? _heels.GetLocalOffsetJson() : null,
                MoodlesData:   _moodles.IsAvailable   ? _moodles.GetStatusJson(local.Address) : null,
                PetNamesData:  _petnames.IsAvailable  ? _petnames.GetLocalNamesJson() : null,
                BrioData:      _brio.IsAvailable      ? _brio.GetPoseJson(local.ObjectIndex) : null);
        }, cancellationToken: ct).ConfigureAwait(false);

        if (snapshot is null) return (Empty(), new Dictionary<string, string>());

        // Phase 2 (off tick): hash files + cache writes. The hash cache (mtime+size keyed) makes
        // recurring scans nearly free; first scan reads everything from disk. Heavy but doesn't
        // freeze the game thread.
        return await Task.Run(() =>
        {
            var appearances = new Dictionary<AethernetObjectKind, ObjectAppearanceDto>();
            var hashToPath  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var per in snapshot.PerObject)
            {
                var dto = new ObjectAppearanceDto { ManipulationData = per.ManipulationData };
                int swap = 0, replace = 0, missing = 0;
                foreach (var (gamePath, actualPath) in per.Resources)
                {
                    if (!Path.IsPathRooted(actualPath))
                    {
                        dto.FileSwaps[gamePath] = actualPath;
                        swap++;
                        continue;
                    }
                    if (!File.Exists(actualPath)) { missing++; continue; }
                    try
                    {
                        var (hash, cachePath) = _cache.Ingest(actualPath);
                        dto.FileReplacements[gamePath] = hash;
                        hashToPath[hash] = cachePath;
                        replace++;
                    }
                    catch (Exception ex) { _log.LogDebug(ex, "failed to ingest {Path}", actualPath); }
                }
                // Merge mod-walk files (animations/VFX/sounds) for the local player only.
                int animAdded = 0;
                foreach (var (gamePath, actualPath) in per.ModWalkFiles)
                {
                    if (dto.FileReplacements.ContainsKey(gamePath)) continue;
                    if (!File.Exists(actualPath)) continue;
                    try
                    {
                        var (hash, cachePath) = _cache.Ingest(actualPath);
                        dto.FileReplacements[gamePath] = hash;
                        hashToPath[hash] = cachePath;
                        animAdded++;
                    }
                    catch (Exception ex) { _log.LogDebug(ex, "failed to ingest mod file {Path}", actualPath); }
                }
                if (animAdded > 0)
                    _log.LogInformation("CollectForObject(idx={Idx}): +{Anim} mod-walk files (animations/VFX)", per.ObjectIndex, animAdded);
                _log.LogInformation("CollectForObject(idx={Idx}): total={Total} swap={Swap} replace={Replace} missing={Missing}",
                    per.ObjectIndex, per.Resources.Count, swap, replace, missing);
                appearances[per.Kind] = dto;
            }

            // Build the DTO with DataVersion = 0 so the hash only depends on actual character data.
            // If DataVersion were included, it'd bump on every collect and the hash would never
            // match across consecutive calls — defeating the "skip push when nothing changed"
            // dedupe in SyncOrchestrator and causing constant unnecessary pushes.
            var fullDto = new CharacterDataDto
            {
                DataVersion       = 0,
                Appearances       = appearances,
                GlamourerData     = snapshot.GlamourerData,
                CustomizePlusData = snapshot.CustomizeData,
                HonorificTitle    = snapshot.HonorificData,
                HeelsOffset       = snapshot.HeelsData,
                MoodlesData       = snapshot.MoodlesData,
                PetNamesData      = snapshot.PetNamesData,
                BrioData          = snapshot.BrioData,
            };
            var stableHash = ComputeStableHash(fullDto);
            // Assign DataVersion + DataHash after hashing so DataVersion doesn't perturb the hash
            // but still gets a unique monotonic value for receivers' dedup-by-version check.
            fullDto = fullDto with
            {
                DataVersion = Interlocked.Increment(ref _version),
                DataHash    = stableHash,
            };
            return (fullDto, hashToPath);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Tick-thread part of per-object collection: calls Penumbra IPC and returns
    /// the (gamePath, actualPath) pairs without doing any file I/O.</summary>
    private PerObjectSnapshot GatherForObject(AethernetObjectKind kind, IGameObject obj)
    {
        var resources = _penumbra.IsAvailable
            ? _penumbra.GetResourcePaths(obj.ObjectIndex).Select(kv => (kv.Key, kv.Value)).ToList()
            : new List<(string, string)>();

        // Mod-walk only for the local player to capture animations/VFX/sounds. IPC calls happen
        // here on the tick; the file enumeration (Directory.EnumerateFiles) is also fast since
        // it's just metadata. Actual hashing of the listed files happens off-tick in Phase 2.
        var modWalk = new List<(string, string)>();
        if (obj.ObjectIndex == 0 && _config.EnableModFileEnumeration && _penumbra.IsAvailable)
        {
            try
            {
                foreach (var pair in _penumbra.EnumerateActiveModFiles(obj.ObjectIndex))
                    modWalk.Add((pair.GamePath, pair.ActualPath));
            }
            catch (Exception ex) { _log.LogWarning(ex, "EnumerateActiveModFiles failed for idx={Idx}", obj.ObjectIndex); }
        }

        return new PerObjectSnapshot(
            kind, obj.ObjectIndex,
            _penumbra.IsAvailable ? _penumbra.GetMetaManipulations(obj.ObjectIndex) : null,
            resources, modWalk);
    }

    private sealed record TickSnapshot(
        int PlayerIdx, nint PlayerAddress,
        List<PerObjectSnapshot> PerObject,
        string? GlamourerData, string? CustomizeData, string? HonorificData,
        string? HeelsData, string? MoodlesData, string? PetNamesData, string? BrioData);

    private sealed record PerObjectSnapshot(
        AethernetObjectKind Kind, int ObjectIndex,
        string? ManipulationData,
        List<(string GamePath, string ActualPath)> Resources,
        List<(string GamePath, string ActualPath)> ModWalkFiles);

    private IEnumerable<(AethernetObjectKind Kind, IGameObject Object)> EnumerateOwnedObjects(IPlayerCharacter player)
    {
        foreach (var obj in _objectTable)
        {
            if (obj is null) continue;
            if (obj.OwnerId != player.GameObjectId) continue;

            var kind = obj.ObjectKind switch
            {
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Mount     => AethernetObjectKind.Mount,
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Companion => AethernetObjectKind.Minion,
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc => AethernetObjectKind.Pet,
                Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc  => AethernetObjectKind.Companion,
                _ => (AethernetObjectKind?)null,
            };
            if (kind is null) continue;
            yield return (kind.Value, obj);
        }
    }

    // Legacy CollectForObject is gone — its logic was inlined into Phase 2 of CollectAsync
    // so that IPC stays on the tick (Phase 1) and file hashing runs off-thread (Phase 2).

    private static CharacterDataDto Empty() => new();

    private static string ComputeStableHash(CharacterDataDto dto)
    {
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        return Sha1Helper.Fnv1aHex(Encoding.UTF8.GetBytes(json));
    }
}
