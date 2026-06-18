using System.ComponentModel.DataAnnotations;

namespace Aethernet.Data.Entities;

public class UserEntity
{
    [Key, MaxLength(32)]
    public string Uid { get; set; } = null!;

    /// <summary>Optional human-readable alias, set after registration (e.g. "miyano").</summary>
    [MaxLength(64)]
    public string? Alias { get; set; }

    /// <summary>PBKDF2 hash of the user's secret key (the actual login credential).</summary>
    [Required]
    public string SecretKeyHash { get; set; } = null!;

    /// <summary>One-time recovery secret shown only at registration (PBKDF2 hashed for storage).</summary>
    public string? RecoverySecretHash { get; set; }

    /// <summary>Discord snowflake if the user has linked their Discord account.</summary>
    [MaxLength(32)]
    public string? DiscordUserId { get; set; }

    public bool IsAdmin     { get; set; }
    public bool IsModerator { get; set; }
    public bool IsBanned    { get; set; }
    public string? BanReason{ get; set; }

    public DateTime CreatedAt    { get; set; }
    public DateTime LastSeenAt   { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Public profile description (Markdown, sanitized client-side).</summary>
    public string? ProfileDescription { get; set; }
    /// <summary>Profile picture as raw base64, capped to ~256 KB.</summary>
    public string? ProfilePictureBase64 { get; set; }
    public bool ProfileIsNsfw    { get; set; }
    public bool ProfileIsFlagged { get; set; }

    /// <summary>Per-user file-server quota override; null = default.</summary>
    public long? FileQuotaBytes { get; set; }

    public ICollection<PairEntity> InitiatedPairs { get; set; } = new List<PairEntity>();
    public ICollection<PairEntity> ReceivedPairs  { get; set; } = new List<PairEntity>();
    public ICollection<GroupPairEntity> GroupMemberships { get; set; } = new List<GroupPairEntity>();
}
