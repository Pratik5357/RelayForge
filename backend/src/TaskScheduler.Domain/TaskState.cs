namespace TaskScheduler.Domain;

public enum TaskState
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    DeadLettered = 4,
    Cancelled = 5
}
