using System.ComponentModel.DataAnnotations;

namespace Aethernet.Data.Entities;

/// <summary>
/// Hard block: <see cref="OwnerUid"/> has blocked <see cref="OtherUid"/>. The hub refuses
/// pair requests in either direction and hides the blocker from the blocked user's pair list.
/// </summary>
public class BlockEntity
{
    public long Id { get; set; }
    [Required, MaxLength(32)] public string OwnerUid { get; set; } = null!;
    [Required, MaxLength(32)] public string OtherUid { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string? Reason { get; set; }
}
