using RelayForge.Domain.Enums;

namespace RelayForge.Domain.Entities;

public class Job
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? ScenarioKey { get; set; }
    public JobState State { get; set; } = JobState.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Set once a user requests cancellation. The job's displayed State is left as-is (usually
    /// Running) while winding down -- there is no separate "Cancelling" JobState. In-flight
    /// tasks finish naturally; not-yet-started tasks are cancelled immediately; once every task
    /// reaches a terminal state, JobOrchestrator finalizes the job to Cancelled regardless of
    /// the normal Succeeded/PartiallyFailed/Failed outcome.
    /// </summary>
    public DateTimeOffset? CancellationRequestedAt { get; set; }

    public ICollection<JobTask> Tasks { get; set; } = new List<JobTask>();
}
