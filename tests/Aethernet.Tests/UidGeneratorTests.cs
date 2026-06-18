using Aethernet.Shared.Identity;
using FluentAssertions;
using Xunit;

namespace Aethernet.Tests;

public class UidGeneratorTests
{
    [Fact]
    public void NewUid_starts_with_u_dash_and_is_16_chars_total()
    {
        var uid = UidGenerator.NewUid();
        uid.Should().StartWith("u-");
        uid.Length.Should().Be(16);   // "u-" + 14 chars
    }

    [Fact]
    public void NewGid_starts_with_g_dash()
    {
        UidGenerator.NewGid().Should().StartWith("g-");
    }

    [Fact]
    public void NewUid_alphabet_excludes_confusables()
    {
        // No i, l, o, u in the alphabet (Crockford-ish minus those).
        for (int i = 0; i < 200; i++)
        {
            var uid = UidGenerator.NewUid()[2..];   // drop "u-"
            uid.Should().NotContain("i");
            uid.Should().NotContain("l");
            uid.Should().NotContain("o");
            uid.Should().NotContain("u");
        }
    }

    [Fact]
    public void NewSecretKey_is_long_enough_to_resist_brute_force()
    {
        var key = UidGenerator.NewSecretKey();
        key.Should().StartWith("k-");
        key.Length.Should().BeGreaterThanOrEqualTo(48);   // payload at least 48 chars
    }

    [Fact]
    public void Generated_ids_are_unique_in_a_small_batch()
    {
        var batch = Enumerable.Range(0, 1000).Select(_ => UidGenerator.NewUid()).ToHashSet();
        batch.Count.Should().Be(1000);
    }
}
