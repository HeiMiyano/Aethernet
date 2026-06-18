using Aethernet.Shared.Identity;
using FluentAssertions;
using Xunit;

namespace Aethernet.Tests;

public class SecretKeyHasherTests
{
    [Fact]
    public void Hash_then_Verify_returns_true_for_same_secret()
    {
        var secret = UidGenerator.NewSecretKey();
        var hash   = SecretKeyHasher.Hash(secret);
        SecretKeyHasher.Verify(secret, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_returns_false_for_wrong_secret()
    {
        var hash = SecretKeyHasher.Hash("correct horse battery staple");
        SecretKeyHasher.Verify("wrong",  hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_produces_different_output_each_call_due_to_random_salt()
    {
        var a = SecretKeyHasher.Hash("same input");
        var b = SecretKeyHasher.Hash("same input");
        a.Should().NotBe(b);          // salt randomized
        SecretKeyHasher.Verify("same input", a).Should().BeTrue();
        SecretKeyHasher.Verify("same input", b).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("v2$100000$AA==$BB==")]   // wrong version prefix
    public void Verify_returns_false_for_malformed_stored_value(string stored)
    {
        SecretKeyHasher.Verify("whatever", stored).Should().BeFalse();
    }
}
