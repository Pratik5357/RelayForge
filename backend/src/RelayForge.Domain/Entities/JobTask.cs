using RelayForge.Domain.Enums;

namespace RelayForge.Domain.Entities;

// Named "JobTask" rather than bare "Task" to avoid colliding with System.Threading.Tasks.Task.
public class JobTask
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    /// <summary>The submitter's key for this task, unique within the job. Dependencies' outputs are passed in by key.</summary>
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TaskState State { get; set; } = TaskState.Pending;

    /// <summary>Which handler runs this task, e.g. "simulated", "http.request".</summary>
    public string Type { get; set; } = "simulated";

    /// <summary>The handler's input, as JSON.</summary>
    public string Payload { get; set; } = "{}";

    /// <summary>What the successful attempt returned, as JSON. Passed to dependents.</summary>
    public string? Output { get; set; }

    /// <summary>Where the handler runs: in the worker's process, a child process, or a container.</summary>
    public TaskIsolation Isolation { get; set; } = TaskIsolation.InProcess;

    /// <summary>One attempt is stopped and counted as a retryable failure after this long.</summary>
    public int TimeoutMs { get; set; }

    /// <summary>Higher runs first. A waiting task's effective priority rises as it ages.</summary>
    public int Priority { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>When the task last became Ready (or, for a retry, became due); workers claim the oldest first.</summary>
    public DateTimeOffset? ReadyAt { get; set; }

    // Reliability
    /// <summary>Number of the latest attempt. Only ever goes up, so it doubles as the fencing token.</summary>
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 4;
    public DateTimeOffset? NextAttemptAt { get; set; }
    public Guid? LeaseOwnerId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid();

    public ICollection<TaskDependency> Dependencies { get; set; } = new List<TaskDependency>();
    public ICollection<TaskDependency> Dependents { get; set; } = new List<TaskDependency>();
    public ICollection<TaskAttempt> Attempts { get; set; } = new List<TaskAttempt>();
}
