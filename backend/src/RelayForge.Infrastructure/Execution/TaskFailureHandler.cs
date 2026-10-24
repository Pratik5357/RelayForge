using Microsoft.Extensions.Options;
using RelayForge.Domain.Dag;
using RelayForge.Domain.Entities;
using RelayForge.Domain.Enums;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// Applies a failed attempt to a task entity in memory: either schedules the next retry
/// (State back to Ready with a future NextAttemptAt, per exponential backoff + jitter -- the
/// claim query skips it until then) or dead-letters it once the retry budget is exhausted.
/// Used both by the immediate execution-failure path and by the reliability sweep's
/// lease-reclaim path, so a crashed task consumes an attempt just like an ordinary failure would.
/// </summary>
public class TaskFailureHandler
{
    private readonly ReliabilityOptions _options;
    private readonly int _agingSeconds;

    public TaskFailureHandler(IOptions<ReliabilityOptions> options, IOptions<FairnessOptions> fairness)
    {
        _options = options.Value;
        _agingSeconds = fairness.Value.AgingSeconds;
    }

    /// <param name="cancellationRequested">
    /// True if the job this task belongs to has a CancellationRequestedAt set. Skips the normal
    /// retry/dead-letter decision entirely and goes straight to Cancelled -- a task that fails
    /// while the job is winding down should not sit through its remaining retry budget.
    /// </param>
    /// <param name="retryAfter">A minimum wait the failing system asked for (e.g. HTTP Retry-After).</param>
    public void Apply(JobTask task, string? errorMessage, bool cancellationRequested = false, TimeSpan? retryAfter = null)
    {
        task.ErrorMessage = Truncate(errorMessage);
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

            if (retryAfter is { } asked && asked > delay)
            {
                delay = asked;
            }

            task.NextAttemptAt = DateTimeOffset.UtcNow.Add(delay);
            task.MarkReady(task.NextAttemptAt.Value, _agingSeconds);
        }
        else
        {
            task.State = TaskState.DeadLettered;
            task.CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public static string? Truncate(string? message) =>
        message is { Length: > 2000 } ? message[..2000] : message;

    /// <summary>The outbox event type and message for a task that just left Running.</summary>
    public static JobEventDraft DescribeOutcome(JobTask task, int attempt) => task.State switch
    {
        TaskState.Succeeded => Draft("TaskSucceeded", $"{task.Name} succeeded (attempt {attempt})."),
        TaskState.Ready => Draft("TaskRetrying", $"{task.Name} failed on attempt {attempt} of {task.MaxAttempts}; retrying."),
        TaskState.DeadLettered => Draft("TaskDeadLettered", $"{task.Name} exhausted its retry budget and was dead-lettered."),
        TaskState.Failed => Draft("TaskFailed", $"{task.Name} failed with an error retrying can't fix."),
        TaskState.Cancelled => Draft("TaskCancelled", $"{task.Name} was cancelled."),
        _ => Draft("TaskChanged", $"{task.Name} is now {task.State}."),
    };

    private static JobEventDraft Draft(string type, string message) => new(type, message);
}
