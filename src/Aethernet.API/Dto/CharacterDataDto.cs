using MessagePack;

namespace Aethernet.API.Dto;

/// <summary>
/// The payload that captures one player's full modded appearance at a moment in time.
/// Compact by design — actual mod file bytes are stored in the file server, referenced here by SHA-1.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record CharacterDataDto
{
    /// <summary>Monotonically increasing per-character version, used to dedupe and order updates.</summary>
    public long DataVersion { get; init; }

    /// <summary>FNV-1a hash of the assembled payload, used as a quick equality check on the receive side.</summary>
    public string DataHash { get; init; } = string.Empty;

    /// <summary>One entry per FFXIV object kind we sync (player, minion, companion, pet, mount).</summary>
    public Dictionary<ObjectKind, ObjectAppearanceDto> Appearances { get; init; } = new();

    /// <summary>Glamourer design string (base64, opaque to the server).</summary>
    public string? GlamourerData { get; init; }

    /// <summary>Customize+ profile (JSON, opaque to the server).</summary>
    public string? CustomizePlusData { get; init; }

    /// <summary>Honorific title payload (JSON, opaque to the server).</summary>
    public string? HonorificTitle { get; init; }

    /// <summary>SimpleHeels offset (JSON, opaque to the server).</summary>
    public string? HeelsOffset { get; init; }

    /// <summary>Moodles JSON status blob.</summary>
    public string? MoodlesData { get; init; }

    /// <summary>Pet Names JSON.</summary>
    public string? PetNamesData { get; init; }

    /// <summary>Brio pose/scenario data, JSON, opaque to the server.</summary>
    public string? BrioData { get; init; }
}

/// <summary>
/// Per-object-kind appearance data: file replacements + Penumbra meta manipulations.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed class ObjectAppearanceDto
{
    /// <summary>Penumbra meta manipulation string (base64 zstd of JSON, opaque to the server).</summary>
    public string? ManipulationData { get; init; }

    /// <summary>Penumbra file replacements: in-game path -> SHA-1 of replacement bytes.</summary>
    public Dictionary<string, string> FileReplacements { get; init; } = new();

    /// <summary>
    /// File swaps — Penumbra can redirect one game path to another game path without uploading bytes.
    /// in-game path -> in-game path. Both sides must already own the target file.
    /// </summary>
    public Dictionary<string, string> FileSwaps { get; init; } = new();
}

public enum ObjectKind : byte
{
    Player    = 0,
    Minion    = 1,
    Companion = 2,
    Pet       = 3,
    Mount     = 4,
}

/// <summary>Envelope wrapping a CharacterDataDto with the intended recipients.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record UserCharaDataMessageDto(
    List<UserDto> Recipients,
    CharacterDataDto CharacterData);

/// <summary>Envelope a client receives — wraps the sender plus the data they sent.</summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record OnlineUserCharaDataMessageDto(
    UserDto User,
    CharacterDataDto CharacterData);
