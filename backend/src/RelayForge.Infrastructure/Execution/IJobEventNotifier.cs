namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// One push notification about a job or task state change. TaskId/TaskName/TaskState are null
/// for a job-level event (e.g. finalization); JobState always reflects the job's current state
/// at the time of the push.
/// </summary>
public record JobChangedEvent(
    Guid JobId,
    string JobState,
    Guid? TaskId,
    string? TaskName,
    string? TaskState,
    string Message);

/// <summary>
/// Abstraction over "tell anyone watching this job that something changed." Kept in
/// Infrastructure (no SignalR/ASP.NET Core dependency here) so the hosted services and
/// orchestrator stay framework-agnostic; the real implementation (backed by a SignalR hub)
/// lives in RelayForge.Api, which is the only project that needs to know about SignalR.
/// </summary>
public interface IJobEventNotifier
{
    Task NotifyAsync(JobChangedEvent evt, CancellationToken cancellationToken);
}

/// <summary>
/// Default no-op so nothing breaks if a composition root forgets to register a real notifier
/// (e.g. in a future test harness).
/// </summary>
public class NullJobEventNotifier : IJobEventNotifier
{
    public Task NotifyAsync(JobChangedEvent evt, CancellationToken cancellationToken) => Task.CompletedTask;
}
