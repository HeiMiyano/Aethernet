using Aethernet.API.Dto;

namespace Aethernet.Server.Services;

public interface IPairResolver
{
    Task<bool> IsActivelyPairedAsync(string ownerUid, string otherUid);
    Task<IReadOnlyList<string>> FilterToActivePairsAsync(string ownerUid, IReadOnlyList<string> candidates);
    Task<IReadOnlyList<ResolvedPair>> GetActivePairsAsync(string ownerUid);
    Task<IReadOnlyList<ResolvedPair>> GetAllVisiblePairsAsync(string ownerUid);
    Task<UserPairDto> BuildUserPairDtoAsync(string ownerUid, string otherUid);
}

/// <summary>Result row for pair queries — flattened from the relational shape.</summary>
public sealed record ResolvedPair(
    string OtherUid,
    string? OtherAlias,
    IndividualPairStatus Status,
    UserPermissions OwnPermissions,
    UserPermissions OtherPermissions,
    List<string> SharedGroups);
