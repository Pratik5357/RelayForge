using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RelayForge.Domain.Dag;
using RelayForge.Domain.Entities;
using RelayForge.Domain.Enums;
using RelayForge.Infrastructure.Telemetry;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// The one way to change a job. <see cref="ExecuteAsync"/> opens a transaction, takes the job's
/// row lock, runs the caller's mutation, then advances the DAG in that same transaction:
/// Pending tasks whose dependencies all succeeded become Ready, permanently-blocked tasks (and,
/// after a cancel request, unclaimed ones) are Cancelled, and the job is finalized once nothing
/// is left to run. Every event the unit produced is numbered and written to the outbox in the
/// same commit, and a NOTIFY wakes workers and the dispatcher once it lands.
///
/// Three guarantees fall out of this:
///   - A state change and its consequences commit together. A worker can't save Succeeded and
///     then crash before its dependents become Ready -- both land, or neither does.
///   - Mutations of one job are serialized. Two tasks finishing at the same moment can't both
///     read a stale picture and both (or neither) finalize the job. Other jobs are unaffected.
///   - An event exists if and only if its change committed (no dual write), and a job's events
///     are numbered without gaps.
/// </summary>
public class JobOrchestrator
{
    private readonly RelayForgeDbContext _db;
    private readonly int _agingSeconds;

    public JobOrchestrator(RelayForgeDbContext db, IOptions<FairnessOptions> fairness)
    {
        _db = db;
        _agingSeconds = fairness.Value.AgingSeconds;
    }

    /// <summary>
    /// Runs <paramref name="mutate"/> under the job's row lock. The mutation changes tracked
    /// entities (it doesn't need to call SaveChanges) and adds any events to record; returning
    /// false rolls everything back. Returns whether the transaction committed.
    /// </summary>
    /// <remarks>
    /// The whole unit re-runs if the connection drops (EnableRetryOnFailure), so the mutation must
    /// load what it needs itself rather than reuse entities from outside: the change tracker is
    /// cleared at the start of every try.
    /// </remarks>
    public async Task<bool> ExecuteAsync(
        Guid jobId,
        Func<List<JobEventDraft>, CancellationToken, Task<bool>> mutate,
        CancellationToken cancellationToken)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        List<JobEventDraft> committedEvents = new();

        var committed = await strategy.ExecuteAsync(async ct =>
        {
            _db.ChangeTracker.Clear();
            var events = new List<JobEventDraft>();

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // Lock order everywhere is job row first, then its task rows, so these units can't
            // deadlock each other. (A submit locks a row that doesn't exist yet; harmless, since
            // nothing else knows the new job's id until this commits.)
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $@"SELECT 1 FROM ""Jobs"" WHERE ""Id"" = {jobId} FOR UPDATE", ct);

            if (!await mutate(events, ct))
            {
                await tx.RollbackAsync(ct);
                return false;
            }

            await _db.SaveChangesAsync(ct);

            var promotedAny = await AdvanceAsync(jobId, events, ct);

            // NOTIFY is queued now and delivered only if the transaction commits. One round trip.
            var channels = new List<string>();
            if (promotedAny) channels.Add($"pg_notify('{PgChannels.Ready}', '')");
            if (events.Count > 0) channels.Add($"pg_notify('{PgChannels.Outbox}', '')");

            if (channels.Count > 0)
            {
                await _db.Database.ExecuteSqlRawAsync($"SELECT {string.Join(", ", channels)}", ct);
            }

            await tx.CommitAsync(ct);
            committedEvents = events;
            return true;
        }, cancellationToken);

        RelayForgeTelemetry.DeadLettered.Add(committedEvents.Count(e => e.Type == "TaskDeadLettered"));
        return committed;
    }

    /// <returns>Whether any task was promoted to Ready.</returns>
    private async Task<bool> AdvanceAsync(Guid jobId, List<JobEventDraft> events, CancellationToken cancellationToken)
    {
        var job = await _db.Jobs
            .Include(j => j.Tasks).ThenInclude(t => t.Dependencies)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var promoted = new List<JobTask>();

        if (job.State is not (JobState.Succeeded or JobState.Failed or JobState.PartiallyFailed or JobState.Cancelled))
        {
            promoted = Advance(job, events, now, _agingSeconds);
        }

        AppendToOutbox(job, events, now);
        await _db.SaveChangesAsync(cancellationToken);
        return promoted.Count > 0;
    }

    private static List<JobTask> Advance(Job job, List<JobEventDraft> events, DateTimeOffset now, int agingSeconds)
    {
        var tasks = job.Tasks.ToList();
        var tasksById = tasks.ToDictionary(t => t.Id);

        var dependsOn = tasks.ToDictionary(
            t => t.Id,
            IReadOnlyList<Guid> (t) => t.Dependencies.Select(d => d.DependsOnJobTaskId).ToList());

        // A job winding down after a cancel request starts nothing new: work that hasn't been
        // claimed yet is called off. Running tasks are told to stop (cooperatively) by their
        // worker's heartbeat or a cancel NOTIFY.
        if (job.CancellationRequestedAt is not null)
        {
            foreach (var t in tasks.Where(t => t.State is TaskState.Pending or TaskState.Ready))
            {
                t.State = TaskState.Cancelled;
                t.CompletedAt = now;
                t.NextAttemptAt = null;
                events.Add(new JobEventDraft("TaskCancelled", $"{t.Name} was cancelled (not yet started).", t.Id, t.Name, nameof(TaskState.Cancelled)));
            }
        }

        // A paused job promotes nothing new; resuming re-runs this.
        var promoted = new List<JobTask>();

        if (job.PausedAt is null)
        {
            var readyIds = DagReadiness.GetReadyKeys(
                dependsOn,
                isPending: id => tasksById[id].State == TaskState.Pending,
                isSucceeded: id => tasksById[id].State == TaskState.Succeeded);

            foreach (var id in readyIds)
            {
                tasksById[id].MarkReady(now, agingSeconds);
                promoted.Add(tasksById[id]);
            }
        }

        var evaluation = JobFinalization.Evaluate(tasks
            .Select(t => new JobFinalization.TaskInfo(t.Id, t.State, dependsOn[t.Id]))
            .ToList());

        var newlyBlocked = evaluation.NewlyCancelledTaskIds
            .Select(id => tasksById[id])
            .Where(t => t.State == TaskState.Pending)
            .ToList();

        foreach (var t in newlyBlocked)
        {
            t.State = TaskState.Cancelled;
            t.CompletedAt = now;
            events.Add(new JobEventDraft("TaskCancelled", $"{t.Name} can never run -- a dependency failed or was cancelled.", t.Id, t.Name, nameof(TaskState.Cancelled)));
        }

        if (evaluation.IsFinal)
        {
            // A cancellation request always wins once every task has settled, regardless of
            // the normal Succeeded/PartiallyFailed/Failed computation above.
            job.State = job.CancellationRequestedAt is not null
                ? JobState.Cancelled
                : evaluation.FinalState!.Value;
            job.CompletedAt = now;
            events.Add(new JobEventDraft("JobFinished", $"Job finished: {job.State}."));
        }
        else if (job.State == JobState.Pending &&
                 (newlyBlocked.Count > 0 || tasks.Any(t => t.State is TaskState.Ready or TaskState.Running)))
        {
            job.State = JobState.Running;
            job.StartedAt ??= now;
        }

        return promoted;
    }

    private void AppendToOutbox(Job job, List<JobEventDraft> events, DateTimeOffset now)
    {
        foreach (var draft in events)
        {
            // Under the job's row lock, so per-job numbers are gap-free and in commit order.
            job.EventSequence++;

            var evt = new JobChangedEvent(
                job.Id,
                job.EventSequence,
                draft.Type,
                job.State.ToString(),
                draft.TaskId,
                draft.TaskName,
                draft.TaskState,
                draft.Attempt,
                draft.Message,
                now);

            _db.OutboxEvents.Add(new OutboxEvent
            {
                JobId = job.Id,
                Sequence = evt.Sequence,
                Type = evt.Type,
                Payload = JsonSerializer.Serialize(evt, OutboxJson.Options),
                CreatedAt = now,
            });
        }
    }
}

public static class OutboxJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
