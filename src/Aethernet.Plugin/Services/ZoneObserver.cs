using Aethernet.Plugin.Configuration;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.Logging;

namespace Aethernet.Plugin.Services;

/// <summary>
/// Observes territory transitions and exposes a coarse <see cref="IsInQuietZone"/> flag the
/// orchestrator consults before pushing. When <c>PauseOutsideOfCities</c> is enabled we treat
/// "inside a sanctuary / city / inn / housing ward" as quiet and pause syncing elsewhere.
/// </summary>
public sealed class ZoneObserver : IDisposable
{
    private readonly AethernetConfig _config;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly ILogger<ZoneObserver> _log;

    public bool IsInQuietZone { get; private set; } = true;
    public event Action<bool>? QuietStateChanged;

    public ZoneObserver(
        AethernetConfig config, IClientState clientState, ICondition condition,
        ILogger<ZoneObserver> log)
    {
        _config = config; _clientState = clientState; _condition = condition; _log = log;
        _clientState.TerritoryChanged += OnTerritoryChanged;
        Recompute((ushort)_clientState.TerritoryType);
    }

    // TerritoryChanged switched from Action<ushort> to Action<uint> in Dalamud 15.
    private void OnTerritoryChanged(uint territoryId) => Recompute((ushort)territoryId);

    private void Recompute(ushort territoryId)
    {
        var quiet = SanctuaryTerritories.Contains(territoryId)
                    || (_condition[ConditionFlag.BoundByDuty] == false && territoryId == 0);
        if (quiet == IsInQuietZone) return;
        IsInQuietZone = quiet;
        _log.LogDebug("zone {Zone}: {State}", territoryId, quiet ? "quiet" : "active");
        try { QuietStateChanged?.Invoke(quiet); } catch { /* swallow */ }
    }

    public bool ShouldPauseSyncing => _config.PauseOutsideOfCities && !IsInQuietZone;

    public void Dispose() { _clientState.TerritoryChanged -= OnTerritoryChanged; }

    /// <summary>
    /// City / sanctuary / housing / inn territories. IDs come from the TerritoryType Excel sheet
    /// — extend as Square adds areas. The list errs on the side of "treat as quiet" so a missing
    /// ID just defaults to "active" and the user can pause manually.
    /// </summary>
    private static readonly HashSet<ushort> SanctuaryTerritories = new()
    {
        // --- A Realm Reborn city-states ---
        128, 129,   // Limsa Lominsa Upper / Lower Decks
        130, 131,   // Ul'dah Steps of Nald / Thal
        132, 133,   // New Gridania, Old Gridania
        148,        // Central Shroud (Bentbranch Inn area edge-cases handled by inn flag)

        // --- Heavensward ---
        418, 419,   // The Pillars, Foundation (Ishgard)
        478,        // Idyllshire
        635,        // Rhalgr's Reach
        628,        // Kugane

        // --- Stormblood / Shadowbringers / Endwalker ---
        819, 820,   // The Crystarium, Eulmore
        962,        // Old Sharlayan
        963,        // Radz-at-Han

        // --- Dawntrail ---
        1185,       // Tuliyollal
        1186,       // Solution Nine

        // --- Inns ---
        177, 178, 179,   // Limsa, Gridania, Ul'dah inns
        429,             // Coerthas Western Highlands (Falcon's Nest inn-side)
        844,             // Pendants Personal Suite (Crystarium)
        990,             // Sharlayan Suite

        // --- Housing wards (instanced wards + plots; both quiet) ---
        339, 340, 341,   // Mist (wards)
        342, 343, 344,   // Lavender Beds
        345, 346, 347,   // The Goblet
        641, 642, 643,   // Shirogane
        979, 980, 981,   // Empyreum
        282, 283,        // Plot-instance variants of housing wards
        650, 651,        // FC house instances
        427,             // Mist personal apartment lobby

        // --- Misc safe spots ---
        351,        // The Waking Sands
        388,        // Mor Dhona / Revenant's Toll
        531,        // Diadem (no PvP, low traffic)
    };
}
