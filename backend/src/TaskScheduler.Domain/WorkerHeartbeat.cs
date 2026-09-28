namespace TaskScheduler.Domain;

public sealed class WorkerHeartbeat
{
    public string WorkerId { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
