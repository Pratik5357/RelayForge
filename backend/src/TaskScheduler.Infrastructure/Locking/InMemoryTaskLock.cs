using System.Collections.Concurrent;

namespace TaskScheduler.Infrastructure.Locking;

public sealed class InMemoryTaskLock : ITaskLock
{
    private readonly ConcurrentDictionary<Guid, byte> _held = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(Guid taskId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (!_held.TryAdd(taskId, 0))
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        IAsyncDisposable handle = new Release(() => _held.TryRemove(taskId, out _));
        return Task.FromResult<IAsyncDisposable?>(handle);
    }

    private sealed class Release : IAsyncDisposable
    {
        private readonly Action _release;
        public Release(Action release) => _release = release;
        public ValueTask DisposeAsync()
        {
            _release();
            return ValueTask.CompletedTask;
        }
    }
}
