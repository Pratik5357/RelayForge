using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskScheduler.Domain;
using TaskScheduler.Infrastructure.Queueing;

namespace TaskScheduler.Infrastructure.Hosting;

public sealed class LeaseReaperHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITaskQueue _queue;
    private readonly SchedulerOptions _options;
    private readonly ILogger<LeaseReaperHostedService> _logger;

    public LeaseReaperHostedService(
        IServiceScopeFactory scopeFactory,
        ITaskQueue queue,
        IOptions<SchedulerOptions> options,
        ILogger<LeaseReaperHostedService> logger)
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
                await ReapAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lease reaper failed");
            }

            await Task.Delay(_options.ReaperInterval, stoppingToken);
        }
    }

    private async Task ReapAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        var now = DateTimeOffset.UtcNow;

        var running = await db.JobTasks
            .Where(t => t.State == TaskState.Running)
            .ToListAsync(cancellationToken);

        var expired = running
            .Where(t => t.LeaseUntil != null && t.LeaseUntil < now)
            .ToList();

        foreach (var task in expired)
        {
            task.State = TaskState.Pending;
            task.WorkerId = null;
            task.LeaseUntil = null;
            task.NextAttemptAt = now;
            SchedulerMetrics.LeaseReclaims.Inc();
            _logger.LogWarning("Reclaimed leased task {TaskId} ({Key})", task.Id, task.Key);
        }

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            foreach (var task in expired)
            {
                await _queue.EnqueueAsync(task.Id, cancellationToken);
            }
        }
    }
}
