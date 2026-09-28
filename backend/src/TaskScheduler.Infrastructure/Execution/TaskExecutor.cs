using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prometheus;
using TaskScheduler.Domain;
using TaskScheduler.Infrastructure.Events;
using TaskScheduler.Infrastructure.Handlers;
using TaskScheduler.Infrastructure.Locking;
using TaskScheduler.Infrastructure.Queueing;

namespace TaskScheduler.Infrastructure.Execution;

public sealed class TaskExecutor
{
    private readonly SchedulerDbContext _db;
    private readonly ITaskLock _taskLock;
    private readonly ITaskQueue _queue;
    private readonly IJobEventPublisher _events;
    private readonly IReadOnlyDictionary<string, ITaskHandler> _handlers;
    private readonly ExponentialBackoffRetryPolicy _retry;
    private readonly SchedulerOptions _options;
    private readonly ILogger<TaskExecutor> _logger;

    public TaskExecutor(
        SchedulerDbContext db,
        ITaskLock taskLock,
        ITaskQueue queue,
        IJobEventPublisher events,
        IEnumerable<ITaskHandler> handlers,
        ExponentialBackoffRetryPolicy retry,
        IOptions<SchedulerOptions> options,
        ILogger<TaskExecutor> logger)
    {
        _db = db;
        _taskLock = taskLock;
        _queue = queue;
        _events = events;
        _handlers = handlers.ToDictionary(h => h.Type, StringComparer.OrdinalIgnoreCase);
        _retry = retry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid taskId, CancellationToken cancellationToken)
    {
        await using var acquired = await _taskLock.TryAcquireAsync(taskId, _options.LeaseDuration, cancellationToken);
        if (acquired is null)
        {
            _logger.LogDebug("Could not acquire lock for {TaskId}", taskId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var task = await _db.JobTasks
            .Include(t => t.Job)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.DependsOnTask)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task is null)
        {
            return;
        }

        if (task.State is TaskState.Succeeded or TaskState.DeadLettered or TaskState.Cancelled)
        {
            return;
        }

        if (task.LeaseUntil is { } lease && lease > now)
        {
            return;
        }

        if (task.NextAttemptAt is { } next && next > now)
        {
            return;
        }

        if (task.Dependencies.Any(d => d.DependsOnTask.State != TaskState.Succeeded))
        {
            return;
        }

        task.State = TaskState.Running;
        task.WorkerId = _options.WorkerId;
        task.LeaseUntil = now + _options.LeaseDuration;
        task.AttemptCount += 1;
        task.StartedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        if (task.Job.CancelRequested)
        {
            task.State = TaskState.Cancelled;
            task.LeaseUntil = null;
            task.WorkerId = null;
            await RefreshJobAsync(task.JobId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await PublishAsync(task, "cancelled", cancellationToken);
            return;
        }

        var existing = await _db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == task.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            task.State = TaskState.Succeeded;
            task.ResultJson = existing.ResultJson;
            task.CompletedAt = DateTimeOffset.UtcNow;
            task.LeaseUntil = null;
            SchedulerMetrics.Succeeded.Inc();
            SchedulerMetrics.Executed.Inc();
            await AfterSuccessAsync(task, cancellationToken);
            return;
        }

        if (!_handlers.TryGetValue(task.HandlerType, out var handler))
        {
            await FailAsync(task, $"Unknown handler '{task.HandlerType}'.", cancellationToken);
            return;
        }

        using var timer = SchedulerMetrics.Duration.NewTimer();
        try
        {
            var result = await handler.ExecuteAsync(task.PayloadJson, task.AttemptCount, cancellationToken);
            task.State = TaskState.Succeeded;
            task.ResultJson = result;
            task.LastError = null;
            task.CompletedAt = DateTimeOffset.UtcNow;
            task.LeaseUntil = null;
            task.WorkerId = null;

            _db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Key = task.IdempotencyKey,
                JobTaskId = task.Id,
                ResultJson = result
            });

            SchedulerMetrics.Succeeded.Inc();
            SchedulerMetrics.Executed.Inc();
            _logger.LogInformation("Task {Key} on job {JobId} succeeded (attempt {Attempt})",
                task.Key, task.JobId, task.AttemptCount);
            await AfterSuccessAsync(task, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            {
                await _db.Database.OpenConnectionAsync(cancellationToken);
            }

            await FailAsync(task, ex.Message, cancellationToken);
        }
    }

    private async Task AfterSuccessAsync(JobTask task, CancellationToken cancellationToken)
    {
        await RefreshJobAsync(task.JobId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishAsync(task, "succeeded", cancellationToken);

        var dependents = await _db.TaskDependencies
            .Where(d => d.DependsOnTaskId == task.Id)
            .Select(d => d.JobTaskId)
            .ToListAsync(cancellationToken);

        foreach (var dependentId in dependents)
        {
            if (await IsReadyAsync(dependentId, cancellationToken))
            {
                await _queue.EnqueueAsync(dependentId, cancellationToken);
            }
        }
    }

    private async Task FailAsync(JobTask task, string error, CancellationToken cancellationToken)
    {
        SchedulerMetrics.Failed.Inc();
        SchedulerMetrics.Executed.Inc();
        task.LastError = error;
        task.LeaseUntil = null;
        task.WorkerId = null;

        if (_retry.ShouldRetry(task.AttemptCount, task.MaxAttempts))
        {
            var delay = _retry.GetDelay(task.AttemptCount);
            task.State = TaskState.Pending;
            task.NextAttemptAt = DateTimeOffset.UtcNow + delay;
            _logger.LogWarning("Task {Key} failed attempt {Attempt}/{Max}: {Error}. Retry in {Delay}.",
                task.Key, task.AttemptCount, task.MaxAttempts, error, delay);
        }
        else
        {
            task.State = TaskState.DeadLettered;
            task.CompletedAt = DateTimeOffset.UtcNow;
            _db.DeadLetters.Add(new DeadLetteredTask
            {
                JobId = task.JobId,
                JobTaskId = task.Id,
                TaskKey = task.Key,
                AttemptCount = task.AttemptCount,
                Reason = error
            });
            SchedulerMetrics.DeadLettered.Inc();
            _logger.LogError("Task {Key} dead-lettered after {Attempt} attempts: {Error}",
                task.Key, task.AttemptCount, error);
        }

        await RefreshJobAsync(task.JobId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await PublishAsync(task, task.State == TaskState.DeadLettered ? "dead-lettered" : "retry-scheduled", cancellationToken);
    }

    private async Task<bool> IsReadyAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _db.JobTasks
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.DependsOnTask)
            .FirstAsync(t => t.Id == taskId, cancellationToken);

        if (task.State is not TaskState.Pending)
        {
            return false;
        }

        return task.Dependencies.All(d => d.DependsOnTask.State == TaskState.Succeeded);
    }

    private async Task RefreshJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.Jobs.Include(j => j.Tasks).FirstAsync(j => j.Id == jobId, cancellationToken);

        // A cancel arrives on the API's DbContext, which can be mid-execution here —
        // the tracked copy of the job would still say false, and the job would then
        // settle as Succeeded even though its remaining tasks were cancelled. A scalar
        // projection ignores the identity map and reads what the store actually holds.
        var cancelRequested = await _db.Jobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.CancelRequested)
            .FirstAsync(cancellationToken);
        job.CancelRequested = cancelRequested;

        job.State = JobStateMachine.FromTasks(job.Tasks.ToList(), cancelRequested);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        if (job.State is JobState.Succeeded or JobState.Failed or JobState.PartiallyFailed or JobState.Cancelled)
        {
            job.CompletedAt ??= DateTimeOffset.UtcNow;
        }
    }

    private Task PublishAsync(JobTask task, string message, CancellationToken cancellationToken) =>
        _events.PublishAsync(
            new JobChangedEvent(task.JobId, task.Job.State.ToString(), task.Id, task.Key, task.State.ToString(), message),
            cancellationToken);
}
