namespace TaskScheduler.Domain;

public sealed class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public JobState State { get; set; } = JobState.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public bool CancelRequested { get; set; }

    public ICollection<JobTask> Tasks { get; set; } = new List<JobTask>();
}
