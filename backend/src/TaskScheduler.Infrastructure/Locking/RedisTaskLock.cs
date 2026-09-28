using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace TaskScheduler.Infrastructure.Locking;

public sealed class RedisTaskLock : ITaskLock, IDisposable
{
    private readonly RedLockFactory _factory;

    public RedisTaskLock(string connectionString)
    {
        var multiplexer = ConnectionMultiplexer.Connect(connectionString);
        _factory = RedLockFactory.Create(new List<RedLockMultiplexer> { multiplexer });
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid taskId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var redLock = await _factory.CreateLockAsync($"relayforge:task:{taskId}", ttl);
        if (!redLock.IsAcquired)
        {
            redLock.Dispose();
            return null;
        }

        return new RedLockHandle(redLock);
    }

    public void Dispose() => _factory.Dispose();

    private sealed class RedLockHandle : IAsyncDisposable
    {
        private readonly IRedLock _redLock;
        public RedLockHandle(IRedLock redLock) => _redLock = redLock;
        public ValueTask DisposeAsync()
        {
            _redLock.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
