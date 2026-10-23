using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RelayForge.Api.Contracts;
using RelayForge.Domain.Entities;
using RelayForge.Domain.Enums;
using RelayForge.Infrastructure;
using RelayForge.Infrastructure.Execution;
using RelayForge.Infrastructure.Jobs;

namespace RelayForge.Api.Endpoints;

public static class JobEndpoints
{
    private const int MaxEventsPerPage = 500;
    private const int MaxLogLinesPerPage = 1000;

    public static RouteGroupBuilder MapJobEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", SubmitJobAsync);
        group.MapGet("/", ListJobsAsync);
        group.MapGet("/{id:guid}", GetJobAsync);
        group.MapGet("/{id:guid}/events", GetEventsAsync);
        group.MapPost("/{id:guid}/cancel", (Guid id, JobOperations ops, RelayForgeDbContext db, CancellationToken ct) =>
            RunAsync(id, ops.CancelAsync, db, ct));
        group.MapPost("/{id:guid}/pause", (Guid id, JobOperations ops, RelayForgeDbContext db, CancellationToken ct) =>
            RunAsync(id, ops.PauseAsync, db, ct));
        group.MapPost("/{id:guid}/resume", (Guid id, JobOperations ops, RelayForgeDbContext db, CancellationToken ct) =>
            RunAsync(id, ops.ResumeAsync, db, ct));

        return group;
    }

    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/logs", GetLogsAsync);
        group.MapPost("/{id:guid}/redrive", RedriveAsync);
        return group;
    }

    /// <summary>
    /// Accepts an optional Idempotency-Key header: resending the same key and body returns the
    /// original job (200) instead of creating another; reusing a key with a different body is 422.
    /// </summary>
    private static async Task<IResult> SubmitJobAsync(
        SubmitJobRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        JobSubmissionService submissions,
        RelayForgeDbContext db,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is { Length: > 200 })
        {
            return Results.Problem(detail: "Idempotency-Key is longer than 200 characters.", statusCode: StatusCodes.Status400BadRequest, title: "Invalid job");
        }

        SubmitResult result;

        try
        {
            result = await submissions.SubmitAsync(
                request,
                SubmitContext.Api with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
                cancellationToken);
        }
        catch (JobValidationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: ex.Title);
        }
        catch (IdempotencyConflictException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity, title: "Idempotency key reused");
        }

        var detail = await LoadJobDetailAsync(db, result.JobId, cancellationToken);
        return result.Created ? Results.Created($"/api/jobs/{result.JobId}", detail) : Results.Ok(detail);
    }

    private static async Task<IResult> ListJobsAsync(
        RelayForgeDbContext db,
        CancellationToken cancellationToken,
        int take = 20,
        int skip = 0,
        Guid? definitionId = null)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(skip, 0);

        var query = db.Jobs.AsNoTracking();

        if (definitionId is not null)
        {
            query = query.Where(j => j.DefinitionId == definitionId);
        }

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(SummaryProjection)
            .ToListAsync(cancellationToken);

        return Results.Ok(jobs);
    }

    internal static readonly System.Linq.Expressions.Expression<Func<Job, JobSummaryResponse>> SummaryProjection = j =>
        new JobSummaryResponse(
            j.Id,
            j.Name,
            j.ScenarioKey,
            j.State.ToString(),
            j.CreatedAt,
            j.StartedAt,
            j.CompletedAt,
            j.Tasks.Count,
            j.PausedAt,
            j.DefinitionId,
            j.ScheduledFor);

    private static async Task<IResult> GetJobAsync(Guid id, RelayForgeDbContext db, CancellationToken cancellationToken)
    {
        var detail = await LoadJobDetailAsync(db, id, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    /// <summary>
    /// The job's events after sequence <paramref name="after"/>, oldest first: how a client
    /// catches up after a reconnect or when it sees a gap in the live stream.
    /// </summary>
    private static async Task<IResult> GetEventsAsync(Guid id, RelayForgeDbContext db, CancellationToken cancellationToken, int after = 0)
    {
        if (!await db.Jobs.AnyAsync(j => j.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var payloads = await db.OutboxEvents.AsNoTracking()
            .Where(e => e.JobId == id && e.Sequence > after)
            .OrderBy(e => e.Sequence)
            .Take(MaxEventsPerPage)
            .Select(e => e.Payload)
            .ToListAsync(cancellationToken);

        return Results.Ok(payloads.Select(p => JsonSerializer.Deserialize<JobChangedEvent>(p, OutboxJson.Options)));
    }

    private static async Task<IResult> GetLogsAsync(
        Guid id,
        RelayForgeDbContext db,
        CancellationToken cancellationToken,
        int? attempt = null,
        int after = 0)
    {
        if (!await db.JobTasks.AnyAsync(t => t.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var query = db.TaskLogs.AsNoTracking().Where(l => l.TaskId == id);

        if (attempt is not null)
        {
            query = query.Where(l => l.Attempt == attempt && l.Sequence > after);
        }

        var lines = await query
            .OrderBy(l => l.Attempt).ThenBy(l => l.Sequence)
            .Take(MaxLogLinesPerPage)
            .Select(l => new TaskLogResponse(l.Attempt, l.Sequence, l.Stream, l.Line, l.At))
            .ToListAsync(cancellationToken);

        return Results.Ok(lines);
    }

    private static async Task<IResult> RedriveAsync(
        Guid id,
        RedriveRequest? request,
        JobOperations ops,
        RelayForgeDbContext db,
        CancellationToken cancellationToken)
    {
        var (result, jobId) = await ops.RedriveAsync(id, request?.Attempts, cancellationToken);
        return await ToResultAsync(result, jobId, db, cancellationToken);
    }

    private static async Task<IResult> RunAsync(
        Guid id,
        Func<Guid, CancellationToken, Task<OperationResult>> operation,
        RelayForgeDbContext db,
        CancellationToken cancellationToken) =>
        await ToResultAsync(await operation(id, cancellationToken), id, db, cancellationToken);

    private static async Task<IResult> ToResultAsync(OperationResult result, Guid jobId, RelayForgeDbContext db, CancellationToken cancellationToken)
    {
        if (!result.Found)
        {
            return Results.NotFound();
        }

        if (result.Conflict is not null)
        {
            return Results.Problem(detail: result.Conflict, statusCode: StatusCodes.Status409Conflict, title: "Not allowed now");
        }

        return Results.Ok(await LoadJobDetailAsync(db, jobId, cancellationToken));
    }

    internal static async Task<JobDetailResponse?> LoadJobDetailAsync(
        RelayForgeDbContext db,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking()
            .Include(j => j.Tasks).ThenInclude(t => t.Dependencies)
            .Include(j => j.Tasks).ThenInclude(t => t.Attempts)
            .AsSplitQuery()
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        if (job is null)
        {
            return null;
        }

        var tasks = job.Tasks
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Key)
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
                t.IdempotencyKey,
                t.Key,
                t.Type,
                t.Isolation.ToString(),
                t.TimeoutMs,
                t.Priority,
                JsonDocument.Parse(t.Payload).RootElement,
                t.Output is null ? null : JsonDocument.Parse(t.Output).RootElement,
                t.Attempts
                    .OrderBy(a => a.Attempt)
                    .Select(a => new TaskAttemptResponse(
                        a.Attempt,
                        TaskAttemptService.ShortId(a.WorkerId),
                        a.StartedAt,
                        a.EndedAt,
                        a.Outcome?.ToString(),
                        a.Error,
                        a.RejectedLateResult))
                    .ToList()))
            .ToList();

        var attempts = job.Tasks.SelectMany(t => t.Attempts).ToList();
        var invariants = new InvariantsResponse(
            attempts.Count,
            attempts.Count(a => a.Outcome == AttemptOutcome.Succeeded),
            job.Tasks.Count(t => t.Attempts.Count(a => a.Outcome == AttemptOutcome.Succeeded) > 1),
            attempts.Count(a => a.Outcome == AttemptOutcome.LeaseLost),
            attempts.Count(a => a.RejectedLateResult is not null));

        return new JobDetailResponse(
            job.Id,
            job.Name,
            job.ScenarioKey,
            job.State.ToString(),
            job.CreatedAt,
            job.StartedAt,
            job.CompletedAt,
            job.CancellationRequestedAt,
            tasks,
            job.PausedAt,
            job.TenantId,
            job.DefinitionId,
            job.ScheduledFor,
            job.EventSequence,
            invariants);
    }
}
