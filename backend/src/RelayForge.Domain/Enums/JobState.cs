namespace RelayForge.Domain.Enums;

public enum JobState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    PartiallyFailed,
    Cancelled,
}
