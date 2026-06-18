using Aethernet.API.Dto;
using Aethernet.Data;
using Aethernet.Data.Entities;
using Aethernet.Server.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aethernet.Tests;

public class PairResolverTests
{
    private static AethernetDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AethernetDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AethernetDbContext(options);
    }

    private static async Task<AethernetDbContext> SeedAsync(params string[] uids)
    {
        var db = NewDb();
        foreach (var u in uids)
            db.Users.Add(new UserEntity { Uid = u, SecretKeyHash = "stub", CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task One_sided_pair_is_not_active()
    {
        await using var db = await SeedAsync("u-a", "u-b");
        db.Pairs.Add(new PairEntity { OwnerUid = "u-a", OtherUid = "u-b", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var resolver = new PairResolver(db);
        (await resolver.IsActivelyPairedAsync("u-a", "u-b")).Should().BeFalse();
        (await resolver.IsActivelyPairedAsync("u-b", "u-a")).Should().BeFalse();
    }

    [Fact]
    public async Task Bidirectional_pair_is_active_both_ways()
    {
        await using var db = await SeedAsync("u-a", "u-b");
        db.Pairs.Add(new PairEntity { OwnerUid = "u-a", OtherUid = "u-b", CreatedAt = DateTime.UtcNow });
        db.Pairs.Add(new PairEntity { OwnerUid = "u-b", OtherUid = "u-a", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var resolver = new PairResolver(db);
        (await resolver.IsActivelyPairedAsync("u-a", "u-b")).Should().BeTrue();
        (await resolver.IsActivelyPairedAsync("u-b", "u-a")).Should().BeTrue();
    }

    [Fact]
    public async Task Shared_group_membership_counts_as_active_pair()
    {
        await using var db = await SeedAsync("u-a", "u-b");
        db.Groups.Add(new GroupEntity
        {
            Gid = "g-1", OwnerUid = "u-a", PasswordHash = "stub",
            CreatedAt = DateTime.UtcNow, MemberLimit = 100,
        });
        db.GroupPairs.Add(new GroupPairEntity { Gid = "g-1", Uid = "u-a", Role = GroupPairUserRole.Owner, JoinedAt = DateTime.UtcNow });
        db.GroupPairs.Add(new GroupPairEntity { Gid = "g-1", Uid = "u-b", Role = GroupPairUserRole.Member, JoinedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var resolver = new PairResolver(db);
        (await resolver.IsActivelyPairedAsync("u-a", "u-b")).Should().BeTrue();
    }

    [Fact]
    public async Task FilterToActivePairs_drops_unpaired_uids()
    {
        await using var db = await SeedAsync("u-a", "u-b", "u-c");
        db.Pairs.Add(new PairEntity { OwnerUid = "u-a", OtherUid = "u-b", CreatedAt = DateTime.UtcNow });
        db.Pairs.Add(new PairEntity { OwnerUid = "u-b", OtherUid = "u-a", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var resolver = new PairResolver(db);
        var filtered = await resolver.FilterToActivePairsAsync("u-a", new[] { "u-b", "u-c" });
        filtered.Should().BeEquivalentTo(new[] { "u-b" });
    }

    [Fact]
    public async Task BuildUserPairDto_marks_one_sided_correctly()
    {
        await using var db = await SeedAsync("u-a", "u-b");
        db.Pairs.Add(new PairEntity { OwnerUid = "u-a", OtherUid = "u-b", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var resolver = new PairResolver(db);
        var dto = await resolver.BuildUserPairDtoAsync("u-a", "u-b");
        dto.IndividualPairStatus.Should().Be(IndividualPairStatus.OneSided);
    }
}
