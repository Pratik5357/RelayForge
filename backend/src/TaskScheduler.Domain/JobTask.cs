namespace TaskScheduler.Domain;

public sealed class JobTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;

    /// <summary>Stable name within a job, used to declare dependencies.</summary>
    public string Key { get; set; } = string.Empty;

    public string HandlerType { get; set; } = "echo";
    public string PayloadJson { get; set; } = "{}";

    public TaskState State { get; set; } = TaskState.Pending;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string? WorkerId { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }
    public string? ResultJson { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<TaskDependency> Dependencies { get; set; } = new List<TaskDependency>();
    public ICollection<TaskDependency> Dependents { get; set; } = new List<TaskDependency>();
}
