using System.ComponentModel.DataAnnotations;
using Aethernet.API.Dto;

namespace Aethernet.Data.Entities;

/// <summary>
/// Directed pair edge: <see cref="OwnerUid"/> added <see cref="OtherUid"/>.
/// A bidirectional pair is two rows, one in each direction. The hub treats a pair as "active" only
/// when both directions exist (or when they share at least one group).
/// </summary>
public class PairEntity
{
    public long Id { get; set; }

    [Required, MaxLength(32)]
    public string OwnerUid { get; set; } = null!;
    [Required, MaxLength(32)]
    public string OtherUid { get; set; } = null!;

    public UserPermissions OwnPermissions { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAppliedAt { get; set; }

    public UserEntity? Owner { get; set; }
    public UserEntity? Other { get; set; }
}
