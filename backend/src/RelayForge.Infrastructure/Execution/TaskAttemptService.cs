using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RelayForge.Domain.Entities;
using RelayForge.Domain.Enums;
using RelayForge.Infrastructure.Telemetry;

namespace RelayForge.Infrastructure.Execution;

/// <summary>One attempt a worker now owns. AttemptCount is the attempt's fencing token.</summary>
public record ClaimedTask(Guid Id, Guid JobId, int AttemptCount, DateTimeOffset? ReadyAt, string? TraceParent, string Name, int MaxAttempts);

/// <summary>How an attempt ended, as its worker reports it.</summary>
public sealed record AttemptReport(
    AttemptOutcome Outcome,
    string? Error = null,
    string? OutputJson = null,
    TimeSpan? RetryAfter = null);

public enum LeaseRenewal
{
    Renewed,

    /// <summary>Renewed, but the job has a cancel request: stop the work.</summary>
    CancelRequested,

    /// <summary>This worker no longer owns the attempt.</summary>
    Lost,
}

/// <summary>
/// The database side of one task attempt: claim it, keep its lease alive, record its outcome,
/// or reclaim it after its lease expired. The worker pool and the reliability sweep are loops
/// around these.
///
/// Every write after the claim is fenced: it only applies if the row is still Running under the
/// same lease owner and attempt number. A worker that stalled past its lease (a "zombie") finds
/// that its attempt was reclaimed, and its late result is recorded as rejected instead of
/// overwriting anything.
/// </summary>
public class TaskAttemptService
{
    private readonly RelayForgeDbContext _db;
    private readonly JobOrchestrator _orchestrator;
    private readonly TaskFailureHandler _failureHandler;
    private readonly ReliabilityOptions _reliability;
    private readonly FairnessOptions _fairness;
    private readonly ILogger<TaskAttemptService> _logger;

    public TaskAttemptService(
        RelayForgeDbContext db,
        JobOrchestrator orchestrator,
        TaskFailureHandler failureHandler,
        IOptions<ReliabilityOptions> reliability,
        IOptions<FairnessOptions> fairness,
        ILogger<TaskAttemptService> logger)
    {
        _db = db;
        _orchestrator = orchestrator;
        _failureHandler = failureHandler;
        _reliability = reliability.Value;
        _fairness = fairness.Value;
        _logger = logger;
    }

    /// <summary>
    /// Atomically claims the most urgent due Ready task and opens its attempt record, or returns
    /// null if there is nothing to claim.
    ///
    /// FOR UPDATE SKIP LOCKED lets any number of workers poll at once: each locks a different
    /// row and nobody waits on a row another worker is claiming. Lease times use the database
    /// clock (now()) so they can't be skewed by a worker machine's clock. Order: ClaimOrder, i.e.
    /// priority plus one point per AgingSeconds waited, so old low-priority work can't starve,
    /// served straight from the partial index on Ready rows. Paused jobs are
    /// skipped, and so are tenants at their running-task cap (a soft cap: two workers can both
    /// see one free slot and both claim).
    /// </summary>
    public async Task<ClaimedTask?> TryClaimAsync(Guid workerId, CancellationToken cancellationToken)
    {
        var leaseMs = _reliability.LeaseDurationMs;
        var tenantCap = _fairness.MaxRunningPerTenant;

        var claimed = await _db.Database.SqlQuery<ClaimedTask>($"""
            WITH candidate AS (
                SELECT t."Id"
                FROM   "JobTasks" t
                JOIN   "Jobs" j ON j."Id" = t."JobId"
                WHERE  t."State" = 'Ready'
                  AND  (t."NextAttemptAt" IS NULL OR t."NextAttemptAt" <= now())
                  AND  j."PausedAt" IS NULL
                  AND  ({tenantCap} <= 0 OR j."TenantId" IS NULL OR (
                         SELECT count(*)
                         FROM   "JobTasks" r
                         JOIN   "Jobs" rj ON rj."Id" = r."JobId"
                         WHERE  r."State" = 'Running' AND rj."TenantId" = j."TenantId") < {tenantCap})
                ORDER  BY t."ClaimOrder"
                LIMIT  1
                FOR UPDATE OF t SKIP LOCKED
            ), claimed AS (
                UPDATE "JobTasks" t
                SET    "State"          = 'Running',
                       "LeaseOwnerId"   = {workerId},
                       "LeaseExpiresAt" = now() + make_interval(secs => {leaseMs}::float8 / 1000),
                       "AttemptCount"   = t."AttemptCount" + 1,
                       "StartedAt"      = COALESCE(t."StartedAt", now()),
                       "NextAttemptAt"  = NULL
                FROM   candidate c
                WHERE  t."Id" = c."Id"
                RETURNING t."Id", t."JobId", t."AttemptCount", t."ReadyAt", t."Name", t."MaxAttempts"
            ), opened AS (
                INSERT INTO "TaskAttempts" ("TaskId", "JobId", "Attempt", "WorkerId", "StartedAt")
                SELECT "Id", "JobId", "AttemptCount", {workerId}, now() FROM claimed
            )
            SELECT c."Id", c."JobId", c."AttemptCount", c."ReadyAt", j."TraceParent", c."Name", c."MaxAttempts"
            FROM   claimed c
            JOIN   "Jobs" j ON j."Id" = c."JobId"
            """).ToListAsync(cancellationToken);

        if (claimed.Count == 0)
        {
            return null;
        }

        var claim = claimed[0];

        if (claim.ReadyAt is { } readyAt)
        {
            RelayForgeTelemetry.QueueWait.Record(Math.Max(0, (DateTimeOffset.UtcNow - readyAt).TotalMilliseconds));
        }

        return claim;
    }

    /// <summary>
    /// Records the "started" event for a fresh claim, and moves a Pending job to Running.
    ///
    /// One statement rather than a full locked unit: a start changes no task state and needs no
    /// DAG advance, only the job's next event number. The UPDATE takes the job row's lock for the
    /// length of that one statement, which keeps sequence numbers gap-free without holding the
    /// lock across the several round trips a completion needs. (Under load, sibling tasks of one
    /// job queue on that lock; see docs/LOADTEST.md.)
    /// </summary>
    public async Task MarkStartedAsync(ClaimedTask claim, Guid workerId, CancellationToken cancellationToken)
    {
        // Everything but the per-job sequence, job state and time, which the statement fills in.
        var draft = JsonSerializer.Serialize(new
        {
            jobId = claim.JobId,
            type = "TaskStarted",
            taskId = claim.Id,
            taskName = claim.Name,
            taskState = nameof(TaskState.Running),
            attempt = claim.AttemptCount,
            message = $"{claim.Name} started (attempt {claim.AttemptCount} of {claim.MaxAttempts}) on worker {ShortId(workerId)}.",
        }, OutboxJson.Options);

        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH job AS (
                UPDATE "Jobs"
                SET    "EventSequence" = "EventSequence" + 1,
                       "State"         = CASE WHEN "State" = 'Pending' THEN 'Running' ELSE "State" END,
                       "StartedAt"     = COALESCE("StartedAt", now())
                WHERE  "Id" = {claim.JobId}
                RETURNING "Id", "EventSequence", "State"
            ), event AS (
                INSERT INTO "OutboxEvents" ("JobId", "Sequence", "Type", "Payload", "CreatedAt")
                SELECT "Id", "EventSequence", 'TaskStarted',
                       {draft}::jsonb || jsonb_build_object('sequence', "EventSequence", 'jobState', "State", 'at', now()),
                       now()
                FROM   job
                RETURNING 1
            )
            SELECT pg_notify({PgChannels.Outbox}, '') FROM event
            """, cancellationToken);
    }

    /// <summary>
    /// Extends this attempt's lease by LeaseDurationMs, fenced like every other write. Also
    /// reports a pending job cancellation, so a running task stops within one heartbeat.
    /// </summary>
    public async Task<LeaseRenewal> RenewLeaseAsync(ClaimedTask claim, Guid workerId, CancellationToken cancellationToken)
    {
        var leaseMs = _reliability.LeaseDurationMs;

        var renewed = await _db.Database.SqlQuery<bool>($"""
            UPDATE "JobTasks" t
            SET    "LeaseExpiresAt" = now() + make_interval(secs => {leaseMs}::float8 / 1000)
            WHERE  t."Id" = {claim.Id}
              AND  t."State" = 'Running'
              AND  t."LeaseOwnerId" = {workerId}
              AND  t."AttemptCount" = {claim.AttemptCount}
            RETURNING (SELECT j."CancellationRequestedAt" IS NOT NULL FROM "Jobs" j WHERE j."Id" = t."JobId") AS "Value"
            """).ToListAsync(cancellationToken);

        return renewed.Count == 0 ? LeaseRenewal.Lost
            : renewed[0] ? LeaseRenewal.CancelRequested
            : LeaseRenewal.Renewed;
    }

    /// <summary>
    /// Records how <paramref name="claim"/> ended and advances its job, in one transaction.
    /// Returns false, changing nothing but the attempt's audit record, if this worker no longer
    /// owns the attempt.
    /// </summary>
    public async Task<bool> CompleteAsync(
        ClaimedTask claim,
        Guid workerId,
        AttemptReport report,
        CancellationToken cancellationToken)
    {
        bool accepted;

        try
        {
            accepted = await _orchestrator.ExecuteAsync(claim.JobId, async (events, ct) =>
            {
                var task = await _db.JobTasks.Include(t => t.Job).FirstOrDefaultAsync(t => t.Id == claim.Id, ct);

                // The fence. Under the job lock nothing else can change this row, so this check
                // is final; the concurrency tokens on LeaseOwnerId/AttemptCount back it up.
                if (task is null ||
                    task.State != TaskState.Running ||
                    task.LeaseOwnerId != workerId ||
                    task.AttemptCount != claim.AttemptCount)
                {
                    return false;
                }

                var attempt = await _db.TaskAttempts.FirstOrDefaultAsync(a => a.TaskId == claim.Id && a.Attempt == claim.AttemptCount, ct);
                var cancelRequested = task.Job!.CancellationRequestedAt is not null;
                var now = DateTimeOffset.UtcNow;
                var outcome = report.Outcome;

                switch (outcome)
                {
                    case AttemptOutcome.Succeeded:
                        task.State = TaskState.Succeeded;
                        task.Output = report.OutputJson;
                        task.CompletedAt = now;
                        task.ErrorMessage = null;
                        ClearLease(task);
                        break;

                    case AttemptOutcome.Fatal:
                        task.State = TaskState.Failed;
                        task.CompletedAt = now;
                        task.ErrorMessage = TaskFailureHandler.Truncate(report.Error);
                        ClearLease(task);
                        break;

                    case AttemptOutcome.Cancelled:
                        task.State = TaskState.Cancelled;
                        task.CompletedAt = now;
                        task.ErrorMessage = TaskFailureHandler.Truncate(report.Error);
                        ClearLease(task);
                        break;

                    case AttemptOutcome.Released when !cancelRequested:
                        // A deploy isn't the task's fault: hand it back without spending its
                        // budget. AttemptCount stays (it's the fencing token, so it only goes
                        // up); the budget grows by one instead.
                        task.MarkReady(now, _fairness.AgingSeconds);
                        task.MaxAttempts++;
                        ClearLease(task);
                        break;

                    default:
                        _failureHandler.Apply(task, report.Error, cancelRequested, report.RetryAfter);
                        if (outcome == AttemptOutcome.Released)
                        {
                            outcome = AttemptOutcome.Cancelled;
                        }

                        break;
                }

                if (attempt is not null)
                {
                    attempt.Outcome = outcome;
                    attempt.EndedAt = now;
                    attempt.Error = TaskFailureHandler.Truncate(report.Error);
                    RelayForgeTelemetry.Duration.Record((now - attempt.StartedAt).TotalMilliseconds);
                }

                var draft = outcome == AttemptOutcome.Released
                    ? new JobEventDraft("TaskReleased", $"{task.Name} was handed back by a worker that is shutting down; it will run again without using a retry.")
                    : TaskFailureHandler.DescribeOutcome(task, claim.AttemptCount);

                events.Add(draft with { TaskId = task.Id, TaskName = task.Name, TaskState = task.State.ToString(), Attempt = claim.AttemptCount });

                RelayForgeTelemetry.Attempts.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));

                if (task.State == TaskState.Ready && outcome == AttemptOutcome.Failed)
                {
                    RelayForgeTelemetry.Retries.Add(1);
                }

                return true;
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            accepted = false;
        }

        if (!accepted)
        {
            _logger.LogWarning(
                "Discarding result of stale attempt {Attempt} for task {JobTaskId}: worker {WorkerId} no longer holds its lease",
                claim.AttemptCount, claim.Id, workerId);

            await RecordRejectedResultAsync(claim, report, cancellationToken);
        }

        return accepted;
    }

    /// <summary>
    /// Makes a rejected late result visible: noted on the attempt it came from, and as an event.
    /// It changes no task state; that's the point.
    /// </summary>
    private async Task RecordRejectedResultAsync(ClaimedTask claim, AttemptReport report, CancellationToken cancellationToken)
    {
        await _orchestrator.ExecuteAsync(claim.JobId, async (events, ct) =>
        {
            var attempt = await _db.TaskAttempts.FirstOrDefaultAsync(a => a.TaskId == claim.Id && a.Attempt == claim.AttemptCount, ct);
            var task = await _db.JobTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == claim.Id, ct);

            if (attempt is null || task is null)
            {
                return false;
            }

            attempt.RejectedLateResult = TaskFailureHandler.Truncate(
                report.Error is null ? report.Outcome.ToString() : $"{report.Outcome}: {report.Error}");

            events.Add(new JobEventDraft(
                "StaleResultRejected",
                $"Stale result from attempt {claim.AttemptCount} of {task.Name} rejected: its worker had lost the lease.",
                task.Id,
                task.Name,
                task.State.ToString(),
                claim.AttemptCount));
            return true;
        }, cancellationToken);

        RelayForgeTelemetry.StaleResultsRejected.Add(1);
    }

    /// <summary>
    /// Reclaims every Running task whose lease has expired -- its worker crashed, restarted or
    /// stalled -- routing it through the same retry/dead-letter path as an ordinary failure.
    /// Returns how many were reclaimed.
    /// </summary>
    public async Task<int> ReclaimExpiredAsync(CancellationToken cancellationToken)
    {
        // DateTimeOffset.UtcNow inside a query translates to now(): the same clock the lease was
        // set with.
        var expired = await _db.JobTasks
            .Where(t => t.State == TaskState.Running && t.LeaseExpiresAt != null && t.LeaseExpiresAt < DateTimeOffset.UtcNow)
            .Select(t => new { t.Id, t.JobId })
            .ToListAsync(cancellationToken);

        var reclaimed = 0;

        foreach (var candidate in expired)
        {
            try
            {
                var applied = await _orchestrator.ExecuteAsync(candidate.JobId, async (events, ct) =>
                {
                    // Re-check under the lock: the worker may have finished since the query above.
                    var task = await _db.JobTasks
                        .Include(t => t.Job)
                        .Where(t => t.Id == candidate.Id &&
                                    t.State == TaskState.Running &&
                                    t.LeaseExpiresAt != null &&
                                    t.LeaseExpiresAt < DateTimeOffset.UtcNow)
                        .FirstOrDefaultAsync(ct);

                    if (task is null)
                    {
                        return false;
                    }

                    _logger.LogWarning(
                        "Reclaiming task {JobTaskId} (job {JobId}) attempt {Attempt} -- lease expired, treating as a failed attempt",
                        task.Id, task.JobId, task.AttemptCount);

                    var attemptNumber = task.AttemptCount;
                    var attempt = await _db.TaskAttempts.FirstOrDefaultAsync(a => a.TaskId == task.Id && a.Attempt == attemptNumber, ct);
                    const string error = "Lease expired: the worker crashed, restarted or froze before this attempt finished.";

                    _failureHandler.Apply(task, error, task.Job!.CancellationRequestedAt is not null);

                    if (attempt is not null)
                    {
                        attempt.Outcome = AttemptOutcome.LeaseLost;
                        attempt.EndedAt = DateTimeOffset.UtcNow;
                        attempt.Error = error;
                    }

                    var draft = task.State == TaskState.Ready
                        ? new JobEventDraft("TaskReclaimed", $"{task.Name}'s worker went silent and its lease expired -- reclaimed; it will run again.")
                        : TaskFailureHandler.DescribeOutcome(task, attemptNumber);

                    events.Add(draft with { TaskId = task.Id, TaskName = task.Name, TaskState = task.State.ToString(), Attempt = attemptNumber });
                    return true;
                }, cancellationToken);

                if (applied)
                {
                    reclaimed++;
                    RelayForgeTelemetry.LeasesReclaimed.Add(1);
                    RelayForgeTelemetry.Attempts.Add(1, new KeyValuePair<string, object?>("outcome", nameof(AttemptOutcome.LeaseLost)));
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone else changed the row first; nothing to reclaim.
            }
        }

        return reclaimed;
    }

    private static void ClearLease(JobTask task)
    {
        task.LeaseOwnerId = null;
        task.LeaseExpiresAt = null;
        task.NextAttemptAt = null;
    }

    public static string ShortId(Guid id) => id.ToString("N")[..4];
}
