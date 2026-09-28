using TaskScheduler.Domain;

namespace TaskScheduler.UnitTests;

public class ExponentialBackoffRetryPolicyTests
{
    [Fact]
    public void ShouldRetry_UntilMaxAttempts()
    {
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), jitterFactor: 0);
        Assert.True(policy.ShouldRetry(1, 3));
        Assert.True(policy.ShouldRetry(2, 3));
        Assert.False(policy.ShouldRetry(3, 3));
    }

    [Fact]
    public void GetDelay_DoublesUntilCap()
    {
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), jitterFactor: 0);
        var rng = new Random(1);

        Assert.Equal(100, policy.GetDelay(1, rng).TotalMilliseconds, precision: 0);
        Assert.Equal(200, policy.GetDelay(2, rng).TotalMilliseconds, precision: 0);
        Assert.Equal(250, policy.GetDelay(3, rng).TotalMilliseconds, precision: 0);
    }

    [Fact]
    public void GetDelay_JitterStaysWithinBound()
    {
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5), jitterFactor: 0.2);
        for (var i = 0; i < 20; i++)
        {
            var delay = policy.GetDelay(1);
            Assert.InRange(delay.TotalMilliseconds, 100, 120.0001);
        }
    }
}
