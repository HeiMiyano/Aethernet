using System.ComponentModel.DataAnnotations;

namespace Aethernet.Data.Entities;

/// <summary>
/// One-time token for Discord-bot-driven account recovery: the bot DMs the plaintext token to
/// a Discord user whose ID is already linked to an account, and <c>/claim</c> trades it for a
/// rotated secret key. Short-lived and single-use by design — see <c>Aethernet.DiscordBot</c>.
/// Hashed the same way as <see cref="RefreshTokenEntity"/> (SHA-256 of the plaintext); the
/// plaintext itself is never stored.
/// </summary>
public class DiscordRecoveryClaimEntity
{
    public long Id { get; set; }

    [Required, MaxLength(32)]
    public string DiscordUserId { get; set; } = null!;

    /// <summary>The account this claim resolves to, captured at issue time.</summary>
    [Required, MaxLength(32)]
    public string Uid { get; set; } = null!;

    [Required]
    public string TokenHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}
