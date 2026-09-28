namespace TaskScheduler.Domain;

public enum JobState
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    PartiallyFailed = 4,
    Cancelled = 5
}
