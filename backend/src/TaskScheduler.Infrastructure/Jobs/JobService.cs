using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskScheduler.Domain;
using TaskScheduler.Infrastructure.Queueing;

namespace TaskScheduler.Infrastructure.Jobs;

public sealed record SubmitTaskDto(string Key, string Type, JsonElement? Payload, IReadOnlyList<string>? DependsOn, int? MaxAttempts);

public sealed record SubmitJobRequest(string Name, IReadOnlyList<SubmitTaskDto> Tasks);

public sealed record TaskStatusDto(
    Guid Id,
    string Key,
    string Type,
    string State,
    int AttemptCount,
    int MaxAttempts,
    string? LastError,
    string? ResultJson,
    IReadOnlyList<string> DependsOn);

public sealed record JobStatusDto(
    Guid Id,
    string Name,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<TaskStatusDto> Tasks);

public sealed class JobService
{
    private readonly SchedulerDbContext _db;
    private readonly ITaskQueue _queue;

    public JobService(SchedulerDbContext db, ITaskQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<JobStatusDto> SubmitAsync(SubmitJobRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Job name is required.");
        }

        if (request.Tasks is null || request.Tasks.Count == 0)
        {
            throw new ArgumentException("A job must include at least one task.");
        }

        var nodes = request.Tasks
            .Select(t => new DagNode(t.Key, t.DependsOn ?? Array.Empty<string>()))
            .ToList();
        DagGraph.TopologicalSort(nodes);

        var job = new Job { Name = request.Name.Trim() };
        var byKey = new Dictionary<string, JobTask>(StringComparer.Ordinal);

        foreach (var dto in request.Tasks)
        {
            var payload = dto.Payload?.GetRawText() ?? "{}";
            var task = new JobTask
            {
                Job = job,
                Key = dto.Key,
                HandlerType = string.IsNullOrWhiteSpace(dto.Type) ? "echo" : dto.Type,
                PayloadJson = payload,
                MaxAttempts = dto.MaxAttempts is > 0 ? dto.MaxAttempts.Value : 3,
                IdempotencyKey = $"{job.Id}:{dto.Key}"
            };
            job.Tasks.Add(task);
            byKey[task.Key] = task;
        }

        foreach (var dto in request.Tasks)
        {
            foreach (var dep in dto.DependsOn ?? Array.Empty<string>())
            {
                _db.TaskDependencies.Add(new TaskDependency
                {
                    JobTask = byKey[dto.Key],
                    DependsOnTask = byKey[dep]
                });
            }
        }

        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var task in job.Tasks.Where(t =>
                     !request.Tasks.First(d => d.Key == t.Key).DependsOn?.Any() ?? true))
        {
            await _queue.EnqueueAsync(task.Id, cancellationToken);
        }

        return await GetAsync(job.Id, cancellationToken) ?? throw new InvalidOperationException("Job vanished after insert.");
    }

    public async Task<JobStatusDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await _db.Jobs
            .AsNoTracking()
            .Include(j => j.Tasks)
            .ThenInclude(t => t.Dependencies)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

        return job is null ? null : Map(job);
    }

    public async Task<IReadOnlyList<JobStatusDto>> ListAsync(int take, CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 100);
        var jobs = await _db.Jobs
            .AsNoTracking()
            .Include(j => j.Tasks)
            .ThenInclude(t => t.Dependencies)
            .ToListAsync(cancellationToken);

        return jobs
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .Select(Map)
            .ToList();
    }

    public async Task<JobStatusDto?> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await _db.Jobs.Include(j => j.Tasks).FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
        if (job is null)
        {
            return null;
        }

        job.CancelRequested = true;
        foreach (var task in job.Tasks.Where(t => t.State == TaskState.Pending))
        {
            task.State = TaskState.Cancelled;
        }

        job.State = JobStateMachine.FromTasks(job.Tasks.ToList(), true);
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    private static JobStatusDto Map(Job job)
    {
        var keys = job.Tasks.ToDictionary(t => t.Id, t => t.Key);
        var tasks = job.Tasks.Select(t => new TaskStatusDto(
            t.Id,
            t.Key,
            t.HandlerType,
            t.State.ToString(),
            t.AttemptCount,
            t.MaxAttempts,
            t.LastError,
            t.ResultJson,
            t.Dependencies
                .Select(d => keys.GetValueOrDefault(d.DependsOnTaskId, string.Empty))
                .Where(k => k.Length > 0)
                .ToList())).ToList();

        return new JobStatusDto(job.Id, job.Name, job.State.ToString(), job.CreatedAt, job.UpdatedAt, job.CompletedAt, tasks);
    }
}
