using RelayForge.Domain.Entities;

namespace RelayForge.Infrastructure.Execution;

public record TaskExecutionResult(bool Succeeded, string? ErrorMessage)
{
    public static TaskExecutionResult Success() => new(true, null);

    public static TaskExecutionResult Failure(string error) => new(false, error);
}

/// <summary>
/// Tasks don't do real work: they sleep for SimulatedDurationMs, then either succeed or
/// fail per FailUntilAttempt (null = always succeeds). The caller is expected to have
/// already incremented AttemptCount for this attempt before calling ExecuteAsync (the
/// worker pool's claim step does this atomically), so AttemptCount here is the number of
/// the attempt currently running.
/// </summary>
public class SimulatedTaskExecutor
{
    public async Task<TaskExecutionResult> ExecuteAsync(JobTask task, CancellationToken cancellationToken)
    {
        await Task.Delay(Math.Max(0, task.SimulatedDurationMs), cancellationToken);

        if (task.FailUntilAttempt is null || task.AttemptCount >= task.FailUntilAttempt.Value)
        {
            return TaskExecutionResult.Success();
        }

        return TaskExecutionResult.Failure(
            $"Simulated failure on attempt {task.AttemptCount} (this task succeeds starting attempt {task.FailUntilAttempt}).");
    }
}
