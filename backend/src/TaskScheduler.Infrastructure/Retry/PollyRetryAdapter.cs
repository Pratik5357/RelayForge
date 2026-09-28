using Polly;
using Polly.Retry;
using TaskScheduler.Domain;

namespace TaskScheduler.Infrastructure.Retry;

/// <summary>
/// Maps <see cref="ExponentialBackoffRetryPolicy"/> onto a Polly pipeline so both
/// the hand-rolled math and the library API stay in the codebase.
/// </summary>
public static class PollyRetryAdapter
{
    public static ResiliencePipeline Create(ExponentialBackoffRetryPolicy policy, int maxAttempts)
    {
        var options = new RetryStrategyOptions
        {
            MaxRetryAttempts = Math.Max(0, maxAttempts - 1),
            DelayGenerator = args =>
            {
                var delay = policy.GetDelay(args.AttemptNumber + 1);
                return ValueTask.FromResult<TimeSpan?>(delay);
            }
        };

        return new ResiliencePipelineBuilder()
            .AddRetry(options)
            .Build();
    }
}
