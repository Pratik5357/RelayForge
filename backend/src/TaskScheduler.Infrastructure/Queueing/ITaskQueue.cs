namespace TaskScheduler.Infrastructure.Queueing;

public interface ITaskQueue
{
    Task EnqueueAsync(Guid taskId, CancellationToken cancellationToken);
    int Depth { get; }
}
