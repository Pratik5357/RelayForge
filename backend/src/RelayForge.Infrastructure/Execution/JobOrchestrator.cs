using Microsoft.EntityFrameworkCore;
using RelayForge.Domain.Dag;
using RelayForge.Domain.Enums;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// Drives a job forward: computes which Pending tasks just became ready and enqueues them,
/// marks permanently-blocked tasks Cancelled, and finalizes the job's own state once nothing
/// is left to run. Re-queries the job's tasks/edges from the database on every call rather
/// than keeping an in-memory DAG cache, so correctness stays anchored to the DB even under
/// concurrent completions.
/// </summary>
public class JobOrchestrator
{
    private readonly RelayForgeDbContext _db;
    private readonly InProcessTaskQueue _queue;
    private readonly IJobEventNotifier _notifier;

    public JobOrchestrator(RelayForgeDbContext db, InProcessTaskQueue queue, IJobEventNotifier notifier)
    {
        _db = db;
        _queue = queue;
        _notifier = notifier;
    }

    public Task EnqueueInitialReadyTasksAsync(Guid jobId, CancellationToken cancellationToken) =>
        AdvanceAsync(jobId, cancellationToken);

    public Task OnTaskCompletedAsync(Guid jobId, CancellationToken cancellationToken) =>
        AdvanceAsync(jobId, cancellationToken);

    private async Task AdvanceAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.Jobs
            .Include(j => j.Tasks).ThenInclude(t => t.Dependencies)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            return;
        }

        // Already finalized by an earlier call (e.g. a concurrent completion) -- nothing more to do.
        if (job.State is JobState.Succeeded or JobState.Failed or JobState.PartiallyFailed or JobState.Cancelled)
        {
            return;
        }

        var tasks = job.Tasks.ToList();
        var tasksById = tasks.ToDictionary(t => t.Id);

        var dependsOn = tasks.ToDictionary(
            t => t.Id,
            IReadOnlyList<Guid> (t) => t.Dependencies.Select(d => d.DependsOnJobTaskId).ToList());

        var stateByTaskId = tasks.ToDictionary(t => t.Id, t => t.State);

        var readyIds = DagReadiness.GetReadyKeys(
            dependsOn,
            isPending: id => stateByTaskId[id] == TaskState.Pending,
            isSucceeded: id => stateByTaskId[id] == TaskState.Succeeded);

        // A task can be dependency-ready (all deps succeeded) but still mid-backoff after a
        // prior failed attempt -- NextAttemptAt is owned by the reliability sweep, not
        // re-enqueued here.
        var readyNowIds = readyIds.Where(id => tasksById[id].NextAttemptAt is null).ToList();

        foreach (var taskId in readyNowIds)
        {
            _queue.Enqueue(new TaskWorkItem(jobId, taskId));
        }

        var finalizationInput = tasks
            .Select(t => new JobFinalization.TaskInfo(t.Id, t.State, dependsOn[t.Id]))
            .ToList();

        var evaluation = JobFinalization.Evaluate(finalizationInput);

        var newlyBlocked = evaluation.NewlyCancelledTaskIds
            .Select(id => tasksById[id])
            .Where(t => t.State == TaskState.Pending)
            .ToList();

        foreach (var t in newlyBlocked)
        {
            t.State = TaskState.Cancelled;
            t.CompletedAt = DateTimeOffset.UtcNow;
        }

        if (evaluation.IsFinal)
        {
            // A cancellation request always wins once every task has settled, regardless of
            // the normal Succeeded/PartiallyFailed/Failed computation above.
            job.State = job.CancellationRequestedAt is not null
                ? JobState.Cancelled
                : evaluation.FinalState!.Value;
            job.CompletedAt = DateTimeOffset.UtcNow;
        }
        else if (job.State == JobState.Pending &&
                 (tasks.Any(t => t.State == TaskState.Running) || readyNowIds.Count > 0 || newlyBlocked.Count > 0))
        {
            job.State = JobState.Running;
            job.StartedAt ??= DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var t in newlyBlocked)
        {
            await _notifier.NotifyAsync(
                new JobChangedEvent(jobId, job.State.ToString(), t.Id, t.Name, TaskState.Cancelled.ToString(), $"{t.Name} can never run -- a dependency was dead-lettered or cancelled."),
                cancellationToken);
        }

        if (evaluation.IsFinal)
        {
            await _notifier.NotifyAsync(
                new JobChangedEvent(jobId, job.State.ToString(), null, null, null, $"Job finished: {job.State}."),
                cancellationToken);
        }
    }
}
