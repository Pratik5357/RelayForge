namespace RelayForge.Domain.Enums;

public enum TaskState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    DeadLettered,
    Cancelled,
}
