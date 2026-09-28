namespace TaskScheduler.Infrastructure.Locking;

public interface ITaskLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(Guid taskId, TimeSpan ttl, CancellationToken cancellationToken);
}
