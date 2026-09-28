namespace TaskScheduler.Infrastructure.Events;

public sealed record JobChangedEvent(
    Guid JobId,
    string JobState,
    Guid? TaskId,
    string? TaskKey,
    string? TaskState,
    string Message);

public interface IJobEventPublisher
{
    Task PublishAsync(JobChangedEvent evt, CancellationToken cancellationToken);
}

public sealed class NoOpJobEventPublisher : IJobEventPublisher
{
    public Task PublishAsync(JobChangedEvent evt, CancellationToken cancellationToken) => Task.CompletedTask;
}
