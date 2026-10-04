using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RelayForge.Api.Contracts;
using RelayForge.Domain.Dag;
using RelayForge.Domain.Entities;
using RelayForge.Domain.Enums;
using RelayForge.Infrastructure;
using RelayForge.Infrastructure.Execution;

namespace RelayForge.Api.Endpoints;

public static class JobEndpoints
{
    public static RouteGroupBuilder MapJobEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", SubmitJobAsync);
        group.MapGet("/", ListJobsAsync);
        group.MapGet("/{id:guid}", GetJobAsync);
        group.MapPost("/{id:guid}/cancel", CancelJobAsync);

        return group;
    }

    private static async Task<IResult> SubmitJobAsync(
        SubmitJobRequest request,
        RelayForgeDbContext db,
        JobOrchestrator orchestrator,
        IOptions<ReliabilityOptions> reliabilityOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            SubmitJobRequestValidator.Validate(request);
        }
        catch (DagCycleException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Cycle detected");
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid job");
        }

        var job = new Job
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ScenarioKey = request.ScenarioKey,
            State = JobState.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // Map the client-local task "key" to the generated JobTask id so dependsOn edges
        // (expressed by key in the request) can be persisted as TaskDependency rows.
        var taskIdByKey = request.Tasks.ToDictionary(t => t.Key, _ => Guid.NewGuid());

        var defaultMaxAttempts = reliabilityOptions.Value.DefaultMaxAttempts;

        var jobTasks = request.Tasks.Select(t => new JobTask
        {
            Id = taskIdByKey[t.Key],
            JobId = job.Id,
            Name = t.Name,
            State = TaskState.Pending,
            SimulatedDurationMs = t.SimulatedDurationMs,
            CreatedAt = job.CreatedAt,
            MaxAttempts = t.MaxAttempts ?? defaultMaxAttempts,
            FailUntilAttempt = t.FailUntilAttempt,
        }).ToList();

        var dependencies = request.Tasks
            .SelectMany(t => t.DependsOn.Select(depKey => new TaskDependency
            {
                Id = Guid.NewGuid(),
                JobTaskId = taskIdByKey[t.Key],
                DependsOnJobTaskId = taskIdByKey[depKey],
            }))
            .ToList();

        job.Tasks = jobTasks;

        db.Jobs.Add(job);
        db.TaskDependencies.AddRange(dependencies);
        await db.SaveChangesAsync(cancellationToken);

        await orchestrator.EnqueueInitialReadyTasksAsync(job.Id, cancellationToken);

        var detail = await LoadJobDetailAsync(db, job.Id, cancellationToken);
        return Results.Created($"/api/jobs/{job.Id}", detail);
    }

    private static async Task<IResult> ListJobsAsync(
        RelayForgeDbContext db,
        CancellationToken cancellationToken,
        int take = 20,
        int skip = 0)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var jobs = await db.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(j => new JobSummaryResponse(
                j.Id,
                j.Name,
                j.ScenarioKey,
                j.State.ToString(),
                j.CreatedAt,
                j.StartedAt,
                j.CompletedAt,
                j.Tasks.Count))
            .ToListAsync(cancellationToken);

        return Results.Ok(jobs);
    }

    private static async Task<IResult> GetJobAsync(
        Guid id,
        RelayForgeDbContext db,
        CancellationToken cancellationToken)
    {
        var detail = await LoadJobDetailAsync(db, id, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static readonly JobState[] TerminalJobStates =
    [
        JobState.Succeeded,
        JobState.Failed,
        JobState.PartiallyFailed,
        JobState.Cancelled,
    ];

    private static async Task<IResult> CancelJobAsync(
        Guid id,
        RelayForgeDbContext db,
        JobOrchestrator orchestrator,
        IJobEventNotifier notifier,
        CancellationToken cancellationToken)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        if (job is null)
        {
            return Results.NotFound();
        }

        if (TerminalJobStates.Contains(job.State))
        {
            return Results.Problem(
                detail: $"Job is already {job.State} -- nothing to cancel.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Job already finished");
        }

        // Idempotent: calling cancel twice just re-confirms the same request, it doesn't push
        // CancellationRequestedAt forward.
        job.CancellationRequestedAt ??= DateTimeOffset.UtcNow;

        var now = DateTimeOffset.UtcNow;

        // Not-yet-started work is called off immediately. Already-Running tasks are left
        // alone here -- they finish naturally; TaskFailureHandler and the reliability sweep
        // are what stop a Running task from being retried once CancellationRequestedAt is set.
        var cancelledTasks = await db.JobTasks
            .Where(t => t.JobId == id && t.State == TaskState.Pending)
            .Select(t => new { t.Id, t.Name })
            .ToListAsync(cancellationToken);

        if (cancelledTasks.Count > 0)
        {
            var cancelledIds = cancelledTasks.Select(t => t.Id).ToList();

            await db.JobTasks
                .Where(t => cancelledIds.Contains(t.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.State, TaskState.Cancelled)
                    .SetProperty(t => t.CompletedAt, now)
                    .SetProperty(t => t.NextAttemptAt, (DateTimeOffset?)null),
                    cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var t in cancelledTasks)
        {
            await notifier.NotifyAsync(
                new JobChangedEvent(id, job.State.ToString(), t.Id, t.Name, TaskState.Cancelled.ToString(), $"{t.Name} was cancelled (not yet started)."),
                cancellationToken);
        }

        // Re-evaluate: if nothing was Running, the job can finalize to Cancelled right now.
        await orchestrator.OnTaskCompletedAsync(id, cancellationToken);

        var detail = await LoadJobDetailAsync(db, id, cancellationToken);

        await notifier.NotifyAsync(
            new JobChangedEvent(id, detail!.State, null, null, null, "Cancellation requested."),
            cancellationToken);

        return Results.Ok(detail);
    }

    private static async Task<JobDetailResponse?> LoadJobDetailAsync(
        RelayForgeDbContext db,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await db.Jobs
            .Include(j => j.Tasks).ThenInclude(t => t.Dependencies)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            return null;
        }

        var tasks = job.Tasks
            .OrderBy(t => t.CreatedAt)
            .Select(t => new JobTaskResponse(
                t.Id,
                t.Name,
                t.State.ToString(),
                t.Dependencies.Select(d => d.DependsOnJobTaskId).ToList(),
                t.StartedAt,
                t.CompletedAt,
                t.ErrorMessage,
                t.AttemptCount,
                t.MaxAttempts,
                t.NextAttemptAt,
                t.IdempotencyKey))
            .ToList();

        return new JobDetailResponse(
            job.Id,
            job.Name,
            job.ScenarioKey,
            job.State.ToString(),
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.CancellationRequestedAt,
            tasks);
    }
}
