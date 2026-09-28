using MassTransit;

namespace TaskScheduler.Infrastructure.Queueing;

public sealed class MassTransitTaskQueue : ITaskQueue
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitTaskQueue(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public int Depth => (int)SchedulerMetrics.QueueDepth.Value;

    public async Task EnqueueAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await _publishEndpoint.Publish(new TaskReadyMessage(taskId), cancellationToken);
        SchedulerMetrics.Enqueued.Inc();
    }
}
