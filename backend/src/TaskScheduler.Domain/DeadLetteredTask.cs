namespace TaskScheduler.Domain;

public sealed class DeadLetteredTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Guid JobTaskId { get; set; }
    public string TaskKey { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset FailedAt { get; set; } = DateTimeOffset.UtcNow;
}
