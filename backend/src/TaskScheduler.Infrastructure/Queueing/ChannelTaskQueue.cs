using System.Threading.Channels;

namespace TaskScheduler.Infrastructure.Queueing;

/// <summary>Phase 1/2 in-process queue. Workers consume via <see cref="Reader"/>.</summary>
public sealed class ChannelTaskQueue : ITaskQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    public ChannelReader<Guid> Reader => _channel.Reader;
    public int Depth => _channel.Reader.Count;

    public async Task EnqueueAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync(taskId, cancellationToken);
        SchedulerMetrics.QueueDepth.Set(Depth);
    }
}
