using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RelayForge.Handlers;

namespace RelayForge.Infrastructure.Scheduling;

/// <summary>
/// "retention": deletes runs that finished more than olderThanDays ago, batchSize jobs per
/// statement so no single delete holds locks for long. RelayForge runs it on itself, as the
/// system schedule "relayforge.retention". Payload: { "olderThanDays": 7, "batchSize": 1000 }.
/// Tasks, edges, attempts, logs, outbox events and submit keys go with their job.
/// </summary>
public sealed class RetentionHandler : ITaskHandler
{
    public const string TypeName = "retention";

    private readonly IServiceScopeFactory _scopeFactory;

    public RetentionHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public string Type => TypeName;

    public bool InProcessOnly => true;

    private sealed record Options(int OlderThanDays = 7, int BatchSize = 1000);

    public string? Validate(JsonElement payload)
    {
        try
        {
            var options = payload.Deserialize<Options>(HandlerJson.Options);
            return options is null || options.OlderThanDays < 1 || options.BatchSize is < 1 or > 10_000
                ? "retention needs olderThanDays >= 1 and batchSize between 1 and 10000."
                : null;
        }
        catch (JsonException ex)
        {
            return $"Invalid retention payload: {ex.Message}";
        }
    }

    public async Task<TaskResult> ExecuteAsync(TaskContext context)
    {
        var options = context.Payload.Deserialize<Options>(HandlerJson.Options) ?? new Options();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.OlderThanDays);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RelayForgeDbContext>();

        context.Log.Write($"Deleting runs finished before {cutoff:u}, {options.BatchSize} at a time.");
        var total = 0;

        while (true)
        {
            context.Cancellation.ThrowIfCancellationRequested();

            var ids = await db.Jobs
                .Where(j => j.CompletedAt != null && j.CompletedAt < cutoff)
                .OrderBy(j => j.CompletedAt)
                .Select(j => j.Id)
                .Take(options.BatchSize)
                .ToListAsync(context.Cancellation);

            if (ids.Count == 0)
            {
                break;
            }

            // One short transaction per batch. Edges point task-to-task with no cascade, so
            // they go first; the rest cascades from the job row.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await db.TaskDependencies.Where(d => ids.Contains(d.JobTask!.JobId)).ExecuteDeleteAsync(ct);
                await db.Jobs.Where(j => ids.Contains(j.Id)).ExecuteDeleteAsync(ct);
                await tx.CommitAsync(ct);
            }, context.Cancellation);

            total += ids.Count;
            context.Log.Write($"Deleted {ids.Count} run(s) ({total} so far).");
        }

        context.Log.Write($"Done: deleted {total} run(s).");
        return new TaskResult.Success(JsonSerializer.SerializeToElement(new { deleted = total, cutoff }, HandlerJson.Options));
    }
}
