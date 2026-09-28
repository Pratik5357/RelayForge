using MassTransit;
using Microsoft.Extensions.Logging;
using TaskScheduler.Infrastructure.Execution;

namespace TaskScheduler.Infrastructure.Queueing;

public sealed class TaskReadyConsumer : IConsumer<TaskReadyMessage>
{
    private readonly TaskExecutor _executor;
    private readonly ILogger<TaskReadyConsumer> _logger;

    public TaskReadyConsumer(TaskExecutor executor, ILogger<TaskReadyConsumer> logger)
    {
        _executor = executor;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TaskReadyMessage> context)
    {
        _logger.LogInformation("Received TaskReady {TaskId}", context.Message.TaskId);
        await _executor.ExecuteAsync(context.Message.TaskId, context.CancellationToken);
    }
}
