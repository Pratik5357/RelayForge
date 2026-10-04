using RelayForge.Domain.Enums;

namespace RelayForge.Domain.Entities;

// Named "JobTask" rather than bare "Task" to avoid colliding with System.Threading.Tasks.Task.
public class JobTask
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public string Name { get; set; } = string.Empty;
    public TaskState State { get; set; } = TaskState.Pending;
    public int SimulatedDurationMs { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    // Reliability (Phase 2)
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 4;
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? LeaseOwnerId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Null = always succeeds (Phase 1 behavior). Otherwise the task fails on attempts
    /// 1..FailUntilAttempt-1 and succeeds on attempt FailUntilAttempt. If that number is
    /// higher than MaxAttempts, the task exhausts its retry budget and dead-letters instead
    /// of ever succeeding -- this single field drives both the "retries into success" and
    /// "hopeless step dead-letters" demos.
    /// </summary>
    public int? FailUntilAttempt { get; set; }

    public ICollection<TaskDependency> Dependencies { get; set; } = new List<TaskDependency>();
    public ICollection<TaskDependency> Dependents { get; set; } = new List<TaskDependency>();
}
