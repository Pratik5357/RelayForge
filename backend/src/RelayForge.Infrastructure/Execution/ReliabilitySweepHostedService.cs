using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RelayForge.Domain.Enums;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// Runs once immediately on startup (BackgroundService.ExecuteAsync's first loop iteration
/// executes before the first delay) and then on a fixed interval. Two jobs each tick:
///   1. Requeue Pending tasks whose backoff delay has elapsed (NextAttemptAt &lt;= now).
///   2. Reclaim Running tasks whose lease has expired -- the process crashed or restarted
///      mid-execution -- routing them through the same failure/backoff/dead-letter path as
///      an ordinary failure. Running this on startup is what turns "a task stuck Running
///      forever after a restart" (Phase 1's documented limitation) into an actual recovery.
/// </summary>
public class ReliabilitySweepHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InProcessTaskQueue _queue;
    private readonly ReliabilityOptions _options;
    private readonly ILogger<ReliabilitySweepHostedService> _logger;

    public ReliabilitySweepHostedService(
        IServiceScopeFactory scopeFactory,
        InProcessTaskQueue queue,
        IOptions<ReliabilityOptions> options,
        ILogger<ReliabilitySweepHostedService> logger)
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
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Reliability sweep tick failed");
            }

            try
            {
                await Task.Delay(_options.SweepIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutting down
            }
        }
    }

    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RelayForgeDbContext>();
        var failureHandler = scope.ServiceProvider.GetRequiredService<TaskFailureHandler>();
        var notifier = scope.ServiceProvider.GetRequiredService<IJobEventNotifier>();
        var now = DateTimeOffset.UtcNow;

        var dueForRetry = await db.JobTasks
            .Where(t => t.State == TaskState.Pending && t.NextAttemptAt != null && t.NextAttemptAt <= now)
            .Select(t => new { t.Id, t.JobId })
            .ToListAsync(stoppingToken);

        foreach (var t in dueForRetry)
        {
            _queue.Enqueue(new TaskWorkItem(t.JobId, t.Id));
        }

        if (dueForRetry.Count > 0)
        {
            var dueIds = dueForRetry.Select(d => d.Id).ToList();

            // Clear NextAttemptAt so an as-yet-unclaimed retry isn't re-enqueued every tick
            // until a worker actually picks it up. Harmless no-op for a task that a worker
            // already claimed between the SELECT above and this UPDATE.
            await db.JobTasks
                .Where(t => dueIds.Contains(t.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.NextAttemptAt, (DateTimeOffset?)null), stoppingToken);
        }

        var expired = await db.JobTasks
            .Include(t => t.Job)
            .Where(t => t.State == TaskState.Running && t.LeaseExpiresAt != null && t.LeaseExpiresAt < now)
            .ToListAsync(stoppingToken);

        var jobIdsToAdvance = new HashSet<Guid>();

        foreach (var t in expired)
        {
            _logger.LogWarning(
                "Reclaiming task {JobTaskId} (job {JobId}) -- lease expired, treating as a failed attempt",
                t.Id, t.JobId);

            var cancellationRequested = t.Job?.CancellationRequestedAt is not null;
            failureHandler.Apply(t, "Lease expired: the process crashed or restarted before this attempt finished.", cancellationRequested);
            jobIdsToAdvance.Add(t.JobId);
        }

        if (expired.Count > 0)
        {
            await db.SaveChangesAsync(stoppingToken);

            foreach (var t in expired)
            {
                await notifier.NotifyAsync(
                    new JobChangedEvent(
                        t.JobId,
                        t.Job?.State.ToString() ?? "Running",
                        t.Id,
                        t.Name,
                        t.State.ToString(),
                        t.State == TaskState.Cancelled
                            ? $"{t.Name} was cancelled while its lease had expired."
                            : $"{t.Name}'s lease expired (the process likely crashed or restarted) -- reclaimed."),
                    stoppingToken);
            }
        }

        if (jobIdsToAdvance.Count > 0)
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<JobOrchestrator>();

            foreach (var jobId in jobIdsToAdvance)
            {
                await orchestrator.OnTaskCompletedAsync(jobId, stoppingToken);
            }
        }
    }
}
