using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using RelayForge.Domain.Dag;
using RelayForge.Domain.Entities;
using RelayForge.Domain.Enums;
using RelayForge.Handlers;
using RelayForge.Infrastructure.Execution;
using RelayForge.Infrastructure.Telemetry;

namespace RelayForge.Infrastructure.Jobs;

/// <summary>
/// Validates and creates jobs. Used by POST /api/jobs and by the scheduler. A job, its tasks,
/// its root tasks' promotion to Ready, its idempotency key and its first event all commit in one
/// transaction.
/// </summary>
public class JobSubmissionService
{
    private const string UniqueViolation = "23505";

    private readonly RelayForgeDbContext _db;
    private readonly JobOrchestrator _orchestrator;
    private readonly TaskHandlerRegistry _handlers;
    private readonly ReliabilityOptions _reliability;

    public JobSubmissionService(
        RelayForgeDbContext db,
        JobOrchestrator orchestrator,
        TaskHandlerRegistry handlers,
        IOptions<ReliabilityOptions> reliability)
    {
        _db = db;
        _orchestrator = orchestrator;
        _handlers = handlers;
        _reliability = reliability.Value;
    }

    /// <exception cref="JobValidationException">The request is invalid.</exception>
    /// <exception cref="IdempotencyConflictException">The key was used with a different body.</exception>
    public async Task<SubmitResult> SubmitAsync(SubmitJobRequest request, SubmitContext context, CancellationToken cancellationToken)
    {
        var tasks = Validate(request);
        var requestHash = context.IdempotencyKey is null ? null : Hash(request);

        if (context.IdempotencyKey is not null &&
            await FindByKeyAsync(context.IdempotencyKey, requestHash!, cancellationToken) is { } earlier)
        {
            return earlier;
        }

        var job = Build(request, tasks, context);
        var dependencies = request.Tasks
            .SelectMany(t => (t.DependsOn ?? []).Select(depKey => new TaskDependency
            {
                Id = Guid.NewGuid(),
                JobTaskId = job.Tasks.Single(x => x.Key == t.Key).Id,
                DependsOnJobTaskId = job.Tasks.Single(x => x.Key == depKey).Id,
            }))
            .ToList();

        try
        {
            // Insert and promote the root tasks to Ready in one transaction: a crash right after
            // submit can't leave a job whose roots nobody will ever pick up.
            await _orchestrator.ExecuteAsync(job.Id, (events, _) =>
            {
                _db.Jobs.Add(job);
                _db.TaskDependencies.AddRange(dependencies);

                if (context.IdempotencyKey is not null)
                {
                    _db.SubmitKeys.Add(new SubmitKey
                    {
                        Key = context.IdempotencyKey,
                        RequestHash = requestHash!,
                        JobId = job.Id,
                        CreatedAt = job.CreatedAt,
                    });
                }

                events.Add(new JobEventDraft("JobSubmitted", context.DefinitionId is null
                    ? $"Job submitted with {job.Tasks.Count} task(s)."
                    : $"Scheduled run for {context.ScheduledFor:u} submitted with {job.Tasks.Count} task(s)."));
                return Task.FromResult(true);
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Lost a race to an identical request: the unique key/schedule index is the arbiter.
            if (context.IdempotencyKey is not null &&
                await FindByKeyAsync(context.IdempotencyKey, requestHash!, cancellationToken) is { } raced)
            {
                return raced;
            }

            if (context.DefinitionId is { } definitionId)
            {
                var existing = await _db.Jobs.AsNoTracking()
                    .Where(j => j.DefinitionId == definitionId && j.ScheduledFor == context.ScheduledFor)
                    .Select(j => j.Id)
                    .FirstAsync(cancellationToken);
                return new SubmitResult(existing, Created: false);
            }

            throw;
        }

        RelayForgeTelemetry.JobsSubmitted.Add(1, new KeyValuePair<string, object?>("source", context.Source));
        return new SubmitResult(job.Id, Created: true);
    }

    /// <summary>Runs every submit-time check without creating anything.</summary>
    /// <exception cref="JobValidationException">The request is invalid.</exception>
    public void ValidateOnly(SubmitJobRequest request) => Validate(request);

    private async Task<SubmitResult?> FindByKeyAsync(string key, string requestHash, CancellationToken cancellationToken)
    {
        var existing = await _db.SubmitKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Key == key, cancellationToken);

        if (existing is null)
        {
            return null;
        }

        if (existing.RequestHash != requestHash)
        {
            throw new IdempotencyConflictException(key);
        }

        return new SubmitResult(existing.JobId, Created: false);
    }

    private sealed record ValidTask(SubmitJobTaskRequest Request, string Type, JsonElement Payload, TaskIsolation Isolation);

    private List<ValidTask> Validate(SubmitJobRequest request)
    {
        if (request.Tasks is null || request.Tasks.Count == 0)
        {
            throw Invalid("A job must have at least one task.");
        }

        var valid = new List<ValidTask>();

        foreach (var task in request.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Key))
            {
                throw Invalid("Every task must have a non-empty key.");
            }

            if (task.Key.Length > 100)
            {
                throw Invalid($"Task key '{task.Key[..20]}…' is longer than 100 characters.");
            }

            if (string.IsNullOrWhiteSpace(task.Name))
            {
                throw Invalid($"Task '{task.Key}' must have a non-empty name.");
            }

            if (task.SimulatedDurationMs < 0)
            {
                throw Invalid($"Task '{task.Key}' has a negative simulatedDurationMs.");
            }

            if (task.MaxAttempts is < 1)
            {
                throw Invalid($"Task '{task.Key}' has maxAttempts less than 1.");
            }

            if (task.FailUntilAttempt is < 1)
            {
                throw Invalid($"Task '{task.Key}' has failUntilAttempt less than 1.");
            }

            if (task.TimeoutMs is < 100)
            {
                throw Invalid($"Task '{task.Key}' has a timeoutMs under 100.");
            }

            var type = string.IsNullOrWhiteSpace(task.Type) ? "simulated" : task.Type;
            var handler = _handlers.Find(type)
                ?? throw Invalid($"Task '{task.Key}' has unknown type '{type}'. Known types: {string.Join(", ", _handlers.Types.Order())}.");

            var payload = task.Payload is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } given
                ? given
                : type == "simulated"
                    ? JsonSerializer.SerializeToElement(
                        new { durationMs = task.SimulatedDurationMs, failUntilAttempt = task.FailUntilAttempt },
                        HandlerJson.Options)
                    : HandlerJson.EmptyObject;

            if (handler.Validate(payload) is { } payloadError)
            {
                throw Invalid($"Task '{task.Key}': {payloadError}");
            }

            var isolation = TaskIsolation.InProcess;

            if (task.Isolation is not null && !Enum.TryParse(task.Isolation, ignoreCase: true, out isolation))
            {
                throw Invalid($"Task '{task.Key}' has unknown isolation '{task.Isolation}' (use InProcess, Process or Container).");
            }

            if (isolation != TaskIsolation.InProcess && handler.InProcessOnly)
            {
                throw Invalid($"Task '{task.Key}': type '{type}' can only run in-process.");
            }

            valid.Add(new ValidTask(task, type, payload, isolation));
        }

        try
        {
            DagValidator.Validate(request.Tasks.Select(t => new DagNodeInput(t.Key, t.DependsOn ?? [])).ToList());
        }
        catch (DagCycleException ex)
        {
            throw new JobValidationException("Cycle detected", ex.Message);
        }
        catch (ArgumentException ex)
        {
            throw Invalid(ex.Message);
        }

        return valid;
    }

    private Job Build(SubmitJobRequest request, List<ValidTask> tasks, SubmitContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ScenarioKey = request.ScenarioKey,
            State = JobState.Pending,
            CreatedAt = now,
            TenantId = string.IsNullOrWhiteSpace(request.TenantId) ? null : request.TenantId,
            DefinitionId = context.DefinitionId,
            ScheduledFor = context.ScheduledFor,

            // W3C traceparent of the submitting request (or the scheduler's span), carried
            // across the queue to the workers.
            TraceParent = Activity.Current?.Id,
        };

        foreach (var t in tasks)
        {
            job.Tasks.Add(new JobTask
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Key = t.Request.Key,
                Name = t.Request.Name,
                State = TaskState.Pending,
                Type = t.Type,
                Payload = t.Payload.GetRawText(),
                Isolation = t.Isolation,
                TimeoutMs = t.Request.TimeoutMs ?? _reliability.DefaultTimeoutMs,
                Priority = t.Request.Priority ?? request.Priority,
                CreatedAt = now,
                MaxAttempts = t.Request.MaxAttempts ?? _reliability.DefaultMaxAttempts,
            });
        }

        return job;
    }

    private static string Hash(SubmitJobRequest request)
    {
        var json = JsonSerializer.Serialize(request, HandlerJson.Options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static JobValidationException Invalid(string message) => new("Invalid job", message);
}
