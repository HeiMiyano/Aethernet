using Aethernet.Server.Services;
using FluentAssertions;
using Xunit;

namespace Aethernet.Tests;

public class RateLimiterTests
{
    [Fact]
    public void Allows_calls_up_to_the_limit_then_throttles()
    {
        var rl = new InMemoryRateLimiter();
        for (int i = 0; i < 5; i++)
            rl.TryConsume("k", maxPerMinute: 5).Should().BeTrue();
        rl.TryConsume("k", maxPerMinute: 5).Should().BeFalse();
    }

    [Fact]
    public void Keys_are_isolated()
    {
        var rl = new InMemoryRateLimiter();
        for (int i = 0; i < 3; i++)
            rl.TryConsume("a", maxPerMinute: 3).Should().BeTrue();
        rl.TryConsume("a", maxPerMinute: 3).Should().BeFalse();
        rl.TryConsume("b", maxPerMinute: 3).Should().BeTrue();
    }
}
