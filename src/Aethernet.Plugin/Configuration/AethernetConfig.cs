using Aethernet.API.Dto;
using Dalamud.Configuration;

namespace Aethernet.Plugin.Configuration;

public sealed class AethernetConfig : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // Server endpoints are intentionally NOT in config — they're compile-time constants in
    // AethernetServers (see /src/Aethernet.Plugin/AethernetServers.cs). Distributed plugin
    // only connects to official Aethernet infra; running on a fork's server requires editing
    // source + rebuilding. Old configs with AuthServerUrl/HubServerUrl/FileServerUrl JSON
    // properties deserialize fine (System.Text.Json ignores unknown properties by default).

    // ---- credentials ----
    public string? Uid              { get; set; }
    public string? SecretKey        { get; set; }
    public string? AccessToken      { get; set; } // last issued JWT
    public string? RefreshToken     { get; set; }
    public DateTime? AccessTokenExpiresAt { get; set; }

    // ---- behaviour ----
    public bool   AutoConnectOnStartup     { get; set; } = true;
    public bool   PauseOutsideOfCities     { get; set; } = false;
    public bool   ShowDownloadProgressBars { get; set; } = true;
    public int    MaxParallelDownloads     { get; set; } = 4;
    public int    MaxParallelUploads       { get; set; } = 2;
    /// <summary>How long to wait after a Glamourer/Penumbra change before pushing. Higher values
    /// coalesce rapid edits (sliders, gear swaps) into a single push and reduce hub rate-limit hits.</summary>
    public int    DataPushDebounceMs       { get; set; } = 1500;
    /// <summary>Safety-net interval (seconds) for re-collecting and pushing character data. The hash check skips no-op pushes, so this is cheap. Set to 0 to disable.</summary>
    public int    PeriodicSyncIntervalSec  { get; set; } = 30;
    /// <summary>Enumerate every file in every enabled mod (captures animations/VFX/sounds that aren't currently loaded). Can be heavy on disks with multi-GB mod folders — disable if it causes hitching or crashes. Hash cache keeps recurring scans cheap once primed.</summary>
    public bool   EnableModFileEnumeration { get; set; } = true;
    /// <summary>How long to silence push attempts after the hub returns rate_limited. Server's bucket refills during this window.</summary>
    public int    RateLimitBackoffSec      { get; set; } = 10;

    // ---- file cache ----
    public string FileCacheDirectory       { get; set; } = "";  // resolved at runtime under %AppData%\Aethernet
    public long   MaxCacheSizeBytes        { get; set; } = 30L * 1024 * 1024 * 1024; // 30 GiB

    // ---- defaults for new pairs ----
    public UserPermissions DefaultPairPermissions { get; set; } = UserPermissions.None;

    // ---- profile ----
    public string? ProfileBio              { get; set; }
    public bool    ProfileIsNsfw           { get; set; }
    public string? ProfilePictureBase64    { get; set; }

    // ---- UI state ----
    public bool ShowMainWindow             { get; set; } = true;

    // ---- theming ----
    /// <summary>RGBA accent color used for highlights, the connected dot, and button hover. Defaults to a calm blue.</summary>
    public float[] AccentColor { get; set; } = new[] { 0.20f, 0.78f, 0.35f, 1.0f };
    // UiScale was removed — it mutated the global ImGui style every frame, which compounded
    // across frames and broke every other plugin's UI. Use Dalamud's "Interface Scale" in
    // /xlsettings → Look and Feel for per-user UI scaling instead.
    // Old configs with "UiScale": <n> JSON deserialize fine; the property is just dropped.
}
