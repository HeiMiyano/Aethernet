using System.ComponentModel.DataAnnotations;
using Aethernet.API.Dto;

namespace Aethernet.Data.Entities;

public class GroupEntity
{
    [Key, MaxLength(32)]
    public string Gid { get; set; } = null!;

    [MaxLength(64)]
    public string? Alias { get; set; }

    [Required, MaxLength(32)]
    public string OwnerUid { get; set; } = null!;

    /// <summary>PBKDF2 hash of the current join password.</summary>
    [Required]
    public string PasswordHash { get; set; } = null!;

    public GroupPermissions Permissions { get; set; }
    public GroupUserPreferredPermissions DefaultUserPermissions { get; set; }

    public int MemberLimit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PasswordRotatedAt { get; set; }

    public UserEntity? Owner { get; set; }
    public ICollection<GroupPairEntity> Members { get; set; } = new List<GroupPairEntity>();
    public ICollection<GroupBanEntity>  Bans    { get; set; } = new List<GroupBanEntity>();
}

public class GroupPairEntity
{
    public long Id { get; set; }

    [Required, MaxLength(32)] public string Gid { get; set; } = null!;
    [Required, MaxLength(32)] public string Uid { get; set; } = null!;

    public GroupUserPreferredPermissions UserPermissions { get; set; }
    public GroupPairUserRole Role { get; set; }
    public DateTime JoinedAt { get; set; }

    public GroupEntity? Group { get; set; }
    public UserEntity?  User  { get; set; }
}

public class GroupBanEntity
{
    public long Id { get; set; }

    [Required, MaxLength(32)] public string Gid { get; set; } = null!;
    [Required, MaxLength(32)] public string UidBanned { get; set; } = null!;
    [Required, MaxLength(32)] public string UidBannedBy { get; set; } = null!;
    public string? Reason { get; set; }
    public DateTime BannedAt { get; set; }

    public GroupEntity? Group { get; set; }
}
