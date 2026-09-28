namespace TaskScheduler.Domain;

/// <summary>
/// Retry delay = min(cap, base * 2^attempt) * (1 + random jitter in [0, jitterFactor]).
/// Implemented by hand so the backoff math is visible; Polly can wrap the same delays.
/// </summary>
public sealed class ExponentialBackoffRetryPolicy
{
    public TimeSpan BaseDelay { get; }
    public TimeSpan MaxDelay { get; }
    public double JitterFactor { get; }

    public ExponentialBackoffRetryPolicy(
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        double jitterFactor = 0.2)
    {
        if (jitterFactor is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterFactor));
        }

        BaseDelay = baseDelay ?? TimeSpan.FromMilliseconds(200);
        MaxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
        JitterFactor = jitterFactor;
    }

    public bool ShouldRetry(int attemptCount, int maxAttempts) =>
        attemptCount < maxAttempts;

    public TimeSpan GetDelay(int attemptCount, Random? random = null)
    {
        var exp = Math.Pow(2, Math.Max(0, attemptCount - 1));
        var rawMs = Math.Min(MaxDelay.TotalMilliseconds, BaseDelay.TotalMilliseconds * exp);
        var jitter = (random ?? Random.Shared).NextDouble() * JitterFactor;
        return TimeSpan.FromMilliseconds(rawMs * (1 + jitter));
    }
}
