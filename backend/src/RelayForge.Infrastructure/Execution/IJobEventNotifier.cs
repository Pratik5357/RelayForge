namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// One job event as clients receive it. Sequence is per job, starts at 1 and has no gaps, so a
/// client can ignore one it has already seen and fetch the ones it missed
/// (GET /api/jobs/{id}/events?after=N). TaskId/TaskName/TaskState/Attempt are null for a
/// job-level event. JobState is the job's state as of the commit that produced the event.
/// </summary>
public record JobChangedEvent(
    Guid JobId,
    int Sequence,
    string Type,
    string JobState,
    Guid? TaskId,
    string? TaskName,
    string? TaskState,
    int? Attempt,
    string Message,
    DateTimeOffset At);

/// <summary>
/// An event a unit of work wants recorded; JobOrchestrator numbers it and writes it to the
/// outbox in the same transaction.
/// </summary>
public record JobEventDraft(
    string Type,
    string Message,
    Guid? TaskId = null,
    string? TaskName = null,
    string? TaskState = null,
    int? Attempt = null);

/// <summary>
/// Delivers a committed event to anyone watching. Called only by the outbox dispatcher, never
/// directly after a state change, so a push can't happen for a change that rolled back or go
/// missing for one that committed. The real implementation (SignalR) lives in RelayForge.Api.
/// </summary>
public interface IJobEventNotifier
{
    Task NotifyAsync(JobChangedEvent evt, CancellationToken cancellationToken);
}

/// <summary>Default no-op for hosts without live clients (the worker host).</summary>
public class NullJobEventNotifier : IJobEventNotifier
{
    public Task NotifyAsync(JobChangedEvent evt, CancellationToken cancellationToken) => Task.CompletedTask;
}
