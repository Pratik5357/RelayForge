namespace TaskScheduler.Infrastructure;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    public string Database { get; set; } = "Sqlite";
    public string Queue { get; set; } = "Channel";
    public string LockProvider { get; set; } = "InMemory";
    public int WorkerCount { get; set; } = 4;
    public string WorkerId { get; set; } = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan ReaperInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan DispatcherInterval { get; set; } = TimeSpan.FromSeconds(1);
    public bool RunInProcessWorkers { get; set; } = true;
}
