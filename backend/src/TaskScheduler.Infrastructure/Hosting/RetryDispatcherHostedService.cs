using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskScheduler.Domain;
using TaskScheduler.Infrastructure.Queueing;

namespace TaskScheduler.Infrastructure.Hosting;

/// <summary>Pushes retry-due pending tasks back onto the queue after backoff.</summary>
public sealed class RetryDispatcherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITaskQueue _queue;
    private readonly SchedulerOptions _options;
    private readonly ILogger<RetryDispatcherHostedService> _logger;

    public RetryDispatcherHostedService(
        IServiceScopeFactory scopeFactory,
        ITaskQueue queue,
        IOptions<SchedulerOptions> options,
        ILogger<RetryDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retry dispatcher failed");
            }

            await Task.Delay(_options.DispatcherInterval, stoppingToken);
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        var now = DateTimeOffset.UtcNow;

        var pending = await db.JobTasks
            .Where(t => t.State == TaskState.Pending)
            .Where(t => t.LeaseUntil == null)
            .ToListAsync(cancellationToken);

        var due = pending
            .Where(t => t.NextAttemptAt == null || t.NextAttemptAt <= now)
            .Select(t => t.Id)
            .ToList();

        foreach (var id in due)
        {
            await _queue.EnqueueAsync(id, cancellationToken);
        }
    }
}
