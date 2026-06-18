using Aethernet.API.Dto;
using Dalamud.Configuration;

namespace Aethernet.Plugin.Configuration;

public sealed class AethernetConfig : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ---- server endpoints ----
    // Defaults point at the heimiyano test deployment so the wizard pre-populates with
    // working values out of the box. Power users can still edit these in /aethernet settings
    // — e.g. set them back to http://localhost:500{1,2,3} for local dev.
    public string AuthServerUrl { get; set; } = "https://auth-aethernet.heimiyano.com/";
    public string HubServerUrl  { get; set; } = "https://hub-aethernet.heimiyano.com/";
    public string FileServerUrl { get; set; } = "https://files-aethernet.heimiyano.com/";

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
    public int    DataPushDebounceMs       { get; set; } = 250;
    /// <summary>Safety-net interval (seconds) for re-collecting and pushing character data. The hash check skips no-op pushes, so this is cheap. Set to 0 to disable.</summary>
    public int    PeriodicSyncIntervalSec  { get; set; } = 30;
    /// <summary>Enumerate every file in every enabled mod (captures animations/VFX/sounds that aren't currently loaded). Can be heavy on disks with multi-GB mod folders — disable if it causes hitching or crashes. Hash cache keeps recurring scans cheap once primed.</summary>
    public bool   EnableModFileEnumeration { get; set; } = true;

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
    public float UiScale { get; set; } = 1.0f;
}
