using Microsoft.Extensions.Options;
using RelayForge.Domain.Dag;
using RelayForge.Domain.Entities;
using RelayForge.Domain.Enums;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// Applies a failed attempt to a task entity in memory: either schedules the next retry
/// (State back to Pending with NextAttemptAt set, per exponential backoff + jitter) or
/// dead-letters it once the retry budget is exhausted. Used both by the immediate
/// execution-failure path and by the reliability sweep's lease-reclaim path, so a crashed
/// task consumes an attempt just like an ordinary failure would.
/// </summary>
public class TaskFailureHandler
{
    private readonly ReliabilityOptions _options;

    public TaskFailureHandler(IOptions<ReliabilityOptions> options)
    {
        _options = options.Value;
    }

    /// <param name="cancellationRequested">
    /// True if the job this task belongs to has a CancellationRequestedAt set. Skips the normal
    /// retry/dead-letter decision entirely and goes straight to Cancelled -- a task that fails
    /// while the job is winding down should not sit through its remaining retry budget.
    /// </param>
    public void Apply(JobTask task, string? errorMessage, bool cancellationRequested = false)
    {
        task.ErrorMessage = errorMessage;
        task.LeaseOwnerId = null;
        task.LeaseExpiresAt = null;
        task.NextAttemptAt = null;

        if (cancellationRequested)
        {
            task.State = TaskState.Cancelled;
            task.CompletedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (task.AttemptCount < task.MaxAttempts)
        {
            var delay = RetryPolicy.ComputeBackoff(task.AttemptCount, _options.BaseBackoffMs, _options.MaxBackoffMs);
            task.State = TaskState.Pending;
            task.NextAttemptAt = DateTimeOffset.UtcNow.Add(delay);
        }
        else
        {
            task.State = TaskState.DeadLettered;
            task.CompletedAt = DateTimeOffset.UtcNow;
        }
    }
}
