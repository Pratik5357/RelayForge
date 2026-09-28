using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskScheduler.Infrastructure.Execution;
using TaskScheduler.Infrastructure.Queueing;

namespace TaskScheduler.Infrastructure.Hosting;

public sealed class ChannelWorkerHostedService : BackgroundService
{
    private readonly ChannelTaskQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SchedulerOptions _options;
    private readonly ILogger<ChannelWorkerHostedService> _logger;

    public ChannelWorkerHostedService(
        ChannelTaskQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<SchedulerOptions> options,
        ILogger<ChannelWorkerHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = Enumerable.Range(0, Math.Max(1, _options.WorkerCount))
            .Select(i => RunWorkerAsync(i, stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task RunWorkerAsync(int index, CancellationToken stoppingToken)
    {
        _logger.LogInformation("In-process worker {Index} started", index);
        await foreach (var taskId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            SchedulerMetrics.QueueDepth.Set(_queue.Depth);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<TaskExecutor>();
                await executor.ExecuteAsync(taskId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker {Index} failed executing {TaskId}", index, taskId);
            }
        }
    }
}
