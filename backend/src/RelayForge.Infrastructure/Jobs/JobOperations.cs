using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RelayForge.Domain.Enums;
using RelayForge.Infrastructure.Execution;

namespace RelayForge.Infrastructure.Jobs;

/// <summary>The result of an operator action: done, not found, or refused (with why).</summary>
public sealed record OperationResult(bool Found, string? Conflict = null)
{
    public static readonly OperationResult Ok = new(true);
    public static readonly OperationResult NotFound = new(false);

    public static OperationResult Refuse(string why) => new(true, why);

    public bool Succeeded => Found && Conflict is null;
}

/// <summary>
/// Operator actions on jobs and tasks. Each one is a single locked unit through
/// <see cref="JobOrchestrator"/>, so it can't interleave with a completion of the same job.
/// </summary>
public class JobOperations
{
    private static readonly JobState[] TerminalJobStates =
    [
        JobState.Succeeded,
        JobState.Failed,
        JobState.PartiallyFailed,
        JobState.Cancelled,
    ];

    private readonly RelayForgeDbContext _db;
    private readonly JobOrchestrator _orchestrator;
    private readonly ReliabilityOptions _reliability;
    private readonly FairnessOptions _fairness;

    public JobOperations(
        RelayForgeDbContext db,
        JobOrchestrator orchestrator,
        IOptions<ReliabilityOptions> reliability,
        IOptions<FairnessOptions> fairness)
    {
        _db = db;
        _orchestrator = orchestrator;
        _reliability = reliability.Value;
        _fairness = fairness.Value;
    }

    /// <summary>
    /// Requests cancellation. Unclaimed work is called off in the same transaction; running
    /// attempts are told to stop at once through a cancel NOTIFY, and within one heartbeat at
    /// the latest. Idempotent: a second call doesn't move CancellationRequestedAt.
    /// </summary>
    public async Task<OperationResult> CancelAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var result = OperationResult.NotFound;

        await _orchestrator.ExecuteAsync(jobId, async (events, ct) =>
        {
            var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);

            if (job is null)
            {
                result = OperationResult.NotFound;
                return false;
            }

            if (TerminalJobStates.Contains(job.State))
            {
                result = OperationResult.Refuse($"Job is already {job.State} -- nothing to cancel.");
                return false;
            }

            result = OperationResult.Ok;

            if (job.CancellationRequestedAt is not null)
            {
                return false;
            }

            job.CancellationRequestedAt = DateTimeOffset.UtcNow;
            events.Add(new JobEventDraft("JobCancelRequested", "Cancellation requested. Running steps are being stopped."));
            return true;
        }, cancellationToken);

        if (result.Succeeded)
        {
            // Best-effort fast path; the heartbeat notices within a third of the lease anyway.
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_notify({PgChannels.Cancel}, {jobId.ToString()})", cancellationToken);
        }

        return result;
    }

    /// <summary>Running attempts finish; nothing new is claimed or promoted until resume.</summary>
    public Task<OperationResult> PauseAsync(Guid jobId, CancellationToken cancellationToken) =>
        ChangeJobAsync(jobId, job =>
        {
            if (job.PausedAt is not null)
            {
                return (null, null);
            }

            job.PausedAt = DateTimeOffset.UtcNow;
            return (null, new JobEventDraft("JobPaused", "Paused: running steps will finish, nothing new starts."));
        }, cancellationToken);

    public async Task<OperationResult> ResumeAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var result = await ChangeJobAsync(jobId, job =>
        {
            if (job.PausedAt is null)
            {
                return (null, null);
            }

            job.PausedAt = null;
            return (null, new JobEventDraft("JobResumed", "Resumed."));
        }, cancellationToken);

        if (result.Succeeded)
        {
            // Ready tasks were waiting in the table all along; wake workers to claim them.
            await _db.Database.ExecuteSqlRawAsync($"SELECT pg_notify('{PgChannels.Ready}', '')", cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Gives a dead-lettered or fatally failed task a fresh retry budget and moves it back to
    /// Ready. If the job had already finished (PartiallyFailed, Failed), it reopens: the tasks
    /// that were cancelled because they were blocked go back to Pending, and the normal advance
    /// step re-blocks any that still can't run because of some other failure.
    /// </summary>
    /// <param name="attempts">New attempts to allow; defaults to Reliability:DefaultMaxAttempts.</param>
    public async Task<(OperationResult Result, Guid JobId)> RedriveAsync(Guid taskId, int? attempts, CancellationToken cancellationToken)
    {
        var jobId = await _db.JobTasks.Where(t => t.Id == taskId).Select(t => (Guid?)t.JobId).FirstOrDefaultAsync(cancellationToken);

        if (jobId is null)
        {
            return (OperationResult.NotFound, Guid.Empty);
        }

        var budget = Math.Max(1, attempts ?? _reliability.DefaultMaxAttempts);
        var result = OperationResult.NotFound;

        await _orchestrator.ExecuteAsync(jobId.Value, async (events, ct) =>
        {
            var job = await _db.Jobs.Include(j => j.Tasks).FirstAsync(j => j.Id == jobId, ct);
            var task = job.Tasks.First(t => t.Id == taskId);

            if (task.State is not (TaskState.DeadLettered or TaskState.Failed))
            {
                result = OperationResult.Refuse($"Only a DeadLettered or Failed task can be redriven; this one is {task.State}.");
                return false;
            }

            if (job.CancellationRequestedAt is not null)
            {
                result = OperationResult.Refuse("The job was cancelled; submit it again instead.");
                return false;
            }

            var now = DateTimeOffset.UtcNow;

            // AttemptCount keeps counting (it's the fencing token); the budget moves instead.
            task.MarkReady(now, _fairness.AgingSeconds);
            task.MaxAttempts = task.AttemptCount + budget;
            task.NextAttemptAt = null;
            task.CompletedAt = null;
            task.ErrorMessage = null;
            events.Add(new JobEventDraft("TaskRedriven", $"{task.Name} was redriven with {budget} more attempt(s).", task.Id, task.Name, nameof(TaskState.Ready)));

            if (TerminalJobStates.Contains(job.State))
            {
                job.State = JobState.Running;
                job.CompletedAt = null;
                events.Add(new JobEventDraft("JobReopened", "Job reopened by a redrive."));
            }

            // In a job nobody cancelled, a Cancelled task was only ever cancelled because it was
            // blocked. Unblock them all; the advance step re-blocks the ones still stuck.
            foreach (var blocked in job.Tasks.Where(t => t.State == TaskState.Cancelled))
            {
                blocked.State = TaskState.Pending;
                blocked.CompletedAt = null;
            }

            result = OperationResult.Ok;
            return true;
        }, cancellationToken);

        return (result, jobId.Value);
    }

    private async Task<OperationResult> ChangeJobAsync(
        Guid jobId,
        Func<Domain.Entities.Job, (string? Refusal, JobEventDraft? Event)> change,
        CancellationToken cancellationToken)
    {
        var result = OperationResult.NotFound;

        await _orchestrator.ExecuteAsync(jobId, async (events, ct) =>
        {
            var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);

            if (job is null)
            {
                result = OperationResult.NotFound;
                return false;
            }

            if (TerminalJobStates.Contains(job.State))
            {
                result = OperationResult.Refuse($"Job is already {job.State}.");
                return false;
            }

            var (refusal, evt) = change(job);

            if (refusal is not null)
            {
                result = OperationResult.Refuse(refusal);
                return false;
            }

            result = OperationResult.Ok;

            if (evt is null)
            {
                return false;
            }

            events.Add(evt);
            return true;
        }, cancellationToken);

        return result;
    }
}
