using System.Text;
using Aethernet.Shared.Hashing;
using FluentAssertions;
using Xunit;

namespace Aethernet.Tests;

public class Sha1HelperTests
{
    [Fact]
    public void Hashes_known_input_to_known_sha1()
    {
        // SHA1("hello") = AAF4C61DDCC5E8A2DABEDE0F3B482CD9AEA9434D
        Sha1Helper.HashBytes(Encoding.UTF8.GetBytes("hello"))
            .Should().Be("AAF4C61DDCC5E8A2DABEDE0F3B482CD9AEA9434D");
    }

    [Fact]
    public void Stream_hash_matches_byte_hash()
    {
        var bytes = Encoding.UTF8.GetBytes("the quick brown fox");
        using var ms = new MemoryStream(bytes);
        Sha1Helper.HashStream(ms).Should().Be(Sha1Helper.HashBytes(bytes));
    }

    [Fact]
    public void Fnv1aHex_changes_with_input()
    {
        var a = Sha1Helper.Fnv1aHex(Encoding.UTF8.GetBytes("a"));
        var b = Sha1Helper.Fnv1aHex(Encoding.UTF8.GetBytes("b"));
        a.Should().NotBe(b);
        a.Length.Should().Be(16); // 64-bit hex
    }
}
