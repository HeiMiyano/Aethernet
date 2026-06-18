using System.ComponentModel.DataAnnotations;

namespace Aethernet.Data.Entities;

/// <summary>Refresh tokens issued by the auth service — single-use, rotated on every refresh.</summary>
public class RefreshTokenEntity
{
    [Key, MaxLength(64)]
    public string TokenId { get; set; } = null!;   // ulid-like
    public string TokenHash { get; set; } = null!; // sha256 of the actual token value
    [Required, MaxLength(32)]
    public string Uid { get; set; } = null!;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenId { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>User-submitted reports on other users' profiles.</summary>
public class ProfileReportEntity
{
    public long Id { get; set; }
    [Required, MaxLength(32)] public string ReporterUid { get; set; } = null!;
    [Required, MaxLength(32)] public string ReportedUid { get; set; } = null!;
    [Required] public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool Resolved { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>Soft audit log of moderator actions.</summary>
public class AuditLogEntity
{
    public long Id { get; set; }
    [Required, MaxLength(32)] public string ActorUid { get; set; } = null!;
    [Required, MaxLength(64)] public string Action  { get; set; } = null!; // "user.ban", "group.delete", ...
    [MaxLength(32)] public string? TargetUid { get; set; }
    [MaxLength(32)] public string? TargetGid { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Server-wide bans (separate from in-group bans).</summary>
public class BannedUserEntity
{
    [Key, MaxLength(32)]
    public string Uid { get; set; } = null!;
    public string? Reason { get; set; }
    public DateTime BannedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
