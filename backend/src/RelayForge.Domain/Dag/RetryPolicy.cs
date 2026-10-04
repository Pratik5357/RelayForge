namespace RelayForge.Domain.Dag;

/// <summary>
/// Pure exponential-backoff-with-jitter calculation, no DB/clock dependency beyond an
/// injectable Random for deterministic tests.
/// </summary>
public static class RetryPolicy
{
    public static TimeSpan ComputeBackoff(int attemptCount, int baseBackoffMs, int maxBackoffMs, Random? random = null)
    {
        random ??= Random.Shared;

        var raw = baseBackoffMs * Math.Pow(2, Math.Max(0, attemptCount - 1));
        var capped = Math.Min(raw, maxBackoffMs);

        // +/- 20% jitter so a batch of simultaneously-failing tasks doesn't retry in lockstep.
        var jitterFactor = 1 + ((random.NextDouble() * 0.4) - 0.2);
        var withJitter = capped * jitterFactor;

        return TimeSpan.FromMilliseconds(Math.Max(0, withJitter));
    }
}
