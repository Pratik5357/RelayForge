using RelayForge.Domain.Dag;

namespace RelayForge.Domain.Tests;

public class RetryPolicyTests
{
    // Deterministic Random for jitter-free assertions: NextDouble() always returns the seed's
    // first draw for a fixed seed, but different attempt numbers still land at different
    // draws in the sequence -- so instead we zero jitter out by seeding a Random whose first
    // NextDouble() is exactly 0.5 (jitterFactor == 1) isn't guaranteed portable across BCL
    // versions, so assertions below use range checks instead of exact equality.

    [Fact]
    public void ComputeBackoff_GrowsExponentially_WithinJitterBounds()
    {
        var delay1 = RetryPolicy.ComputeBackoff(1, baseBackoffMs: 500, maxBackoffMs: 8000);
        var delay2 = RetryPolicy.ComputeBackoff(2, baseBackoffMs: 500, maxBackoffMs: 8000);
        var delay3 = RetryPolicy.ComputeBackoff(3, baseBackoffMs: 500, maxBackoffMs: 8000);

        // attempt 1 -> ~500ms, attempt 2 -> ~1000ms, attempt 3 -> ~2000ms, each +/-20% jitter.
        AssertWithinJitter(delay1.TotalMilliseconds, 500);
        AssertWithinJitter(delay2.TotalMilliseconds, 1000);
        AssertWithinJitter(delay3.TotalMilliseconds, 2000);
    }

    [Fact]
    public void ComputeBackoff_CapsAtMaxBackoff()
    {
        var delay = RetryPolicy.ComputeBackoff(10, baseBackoffMs: 500, maxBackoffMs: 8000);

        // Even with +20% jitter, should never exceed maxBackoffMs * 1.2.
        Assert.True(delay.TotalMilliseconds <= 8000 * 1.2);
    }

    [Fact]
    public void ComputeBackoff_NeverNegative()
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var delay = RetryPolicy.ComputeBackoff(attempt, baseBackoffMs: 100, maxBackoffMs: 1000);
            Assert.True(delay.TotalMilliseconds >= 0);
        }
    }

    private static void AssertWithinJitter(double actualMs, double expectedBaseMs)
    {
        Assert.InRange(actualMs, expectedBaseMs * 0.8, expectedBaseMs * 1.2);
    }
}
