namespace RelayForge.Domain.Enums;

public enum TaskState
{
    /// <summary>Waiting on at least one dependency.</summary>
    Pending,

    /// <summary>
    /// Every dependency succeeded; claimable by any worker once NextAttemptAt (if set) has
    /// passed. Persisted in the database so readiness survives a process restart.
    /// </summary>
    Ready,
    Running,
    Succeeded,
    Failed,
    DeadLettered,
    Cancelled,
}
