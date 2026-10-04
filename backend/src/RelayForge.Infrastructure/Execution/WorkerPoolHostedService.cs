using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RelayForge.Domain.Enums;

namespace RelayForge.Infrastructure.Execution;

public class WorkerPoolHostedService : BackgroundService
{
    private readonly InProcessTaskQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkerPoolHostedService> _logger;
    private readonly int _concurrency;

    public WorkerPoolHostedService(
        InProcessTaskQueue queue,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<WorkerPoolHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _concurrency = configuration.GetValue<int?>("WorkerPool:Concurrency") ?? 4;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumers = Enumerable.Range(0, _concurrency)
            .Select(workerIndex => ConsumeAsync(workerIndex, stoppingToken));

        await Task.WhenAll(consumers);
    }

    private async Task ConsumeAsync(int workerIndex, CancellationToken stoppingToken)
    {
        // Stable for this consumer loop's lifetime -- recorded as JobTask.LeaseOwnerId so a
        // stuck lease can (in principle) be traced back to which in-process consumer held it.
        var workerId = Guid.NewGuid();

        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(item, workerId, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Worker {WorkerIndex} failed processing task {JobTaskId}", workerIndex, item.JobTaskId);
            }
        }
    }

    private async Task ProcessAsync(TaskWorkItem item, Guid workerId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RelayForgeDbContext>();
        var executor = scope.ServiceProvider.GetRequiredService<SimulatedTaskExecutor>();
        var failureHandler = scope.ServiceProvider.GetRequiredService<TaskFailureHandler>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<JobOrchestrator>();
        var notifier = scope.ServiceProvider.GetRequiredService<IJobEventNotifier>();

        var durationLookup = await db.JobTasks
            .Where(t => t.Id == item.JobTaskId)
            .Select(t => (int?)t.SimulatedDurationMs)
            .FirstOrDefaultAsync(stoppingToken);

        if (durationLookup is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var leaseExpiresAt = now.AddMilliseconds(durationLookup.Value + GetLeaseBufferMs(scope));

        // Single conditional update claims the task atomically: if two consumers (or a
        // consumer racing the reliability sweep's reclaim) both try to grab the same task,
        // exactly one UPDATE affects a row -- the loser sees 0 rows affected and backs off
        // instead of double-executing it.
        var claimed = await db.JobTasks
            .Where(t => t.Id == item.JobTaskId && t.State == TaskState.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.State, TaskState.Running)
                .SetProperty(t => t.LeaseOwnerId, workerId)
                .SetProperty(t => t.LeaseExpiresAt, leaseExpiresAt)
                .SetProperty(t => t.StartedAt, t => t.StartedAt ?? now)
                .SetProperty(t => t.AttemptCount, t => t.AttemptCount + 1),
                stoppingToken);

        if (claimed == 0)
        {
            return;
        }

        var task = await db.JobTasks.FirstAsync(t => t.Id == item.JobTaskId, stoppingToken);

        var jobSnapshot = await db.Jobs
            .Where(j => j.Id == item.JobId)
            .Select(j => new { j.State, j.CancellationRequestedAt })
            .FirstAsync(stoppingToken);

        await notifier.NotifyAsync(
            new JobChangedEvent(item.JobId, jobSnapshot.State.ToString(), task.Id, task.Name, TaskState.Running.ToString(), $"{task.Name} started (attempt {task.AttemptCount} of {task.MaxAttempts})."),
            stoppingToken);

        var result = await executor.ExecuteAsync(task, stoppingToken);

        if (result.Succeeded)
        {
            task.State = TaskState.Succeeded;
            task.CompletedAt = DateTimeOffset.UtcNow;
            task.ErrorMessage = null;
            task.LeaseOwnerId = null;
            task.LeaseExpiresAt = null;
        }
        else
        {
            failureHandler.Apply(task, result.ErrorMessage, jobSnapshot.CancellationRequestedAt is not null);
        }

        await db.SaveChangesAsync(stoppingToken);

        await notifier.NotifyAsync(
            new JobChangedEvent(item.JobId, jobSnapshot.State.ToString(), task.Id, task.Name, task.State.ToString(), DescribeOutcome(task)),
            stoppingToken);

        await orchestrator.OnTaskCompletedAsync(item.JobId, stoppingToken);
    }

    private static string DescribeOutcome(RelayForge.Domain.Entities.JobTask task) => task.State switch
    {
        TaskState.Succeeded => $"{task.Name} succeeded.",
        TaskState.Pending => $"{task.Name} failed, retrying (attempt {task.AttemptCount} of {task.MaxAttempts}).",
        TaskState.DeadLettered => $"{task.Name} exhausted its retry budget and was dead-lettered.",
        TaskState.Cancelled => $"{task.Name} was cancelled.",
        _ => $"{task.Name} is now {task.State}.",
    };

    private static int GetLeaseBufferMs(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IOptions<ReliabilityOptions>>().Value.LeaseBufferMs;
}
