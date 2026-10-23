using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// Pushes committed outbox events to live clients (via <see cref="IJobEventNotifier"/>) and
/// marks them dispatched. Runs in every API instance: FOR UPDATE SKIP LOCKED gives each
/// instance different rows, and each instance pushes to the clients connected to it... which
/// is why, without a SignalR backplane, the API stays at one instance (see DESIGN-v2.md).
///
/// Delivery is at-least-once: crashing after a push but before the mark re-sends the event.
/// Clients drop sequences they've already seen, and fetch what they missed after a gap or a
/// reconnect from GET /api/jobs/{id}/events?after=N.
/// </summary>
public class OutboxDispatcherHostedService : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RelaySignals _signals;
    private readonly ILogger<OutboxDispatcherHostedService> _logger;

    public OutboxDispatcherHostedService(IServiceScopeFactory scopeFactory, RelaySignals signals, ILogger<OutboxDispatcherHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _signals = signals;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var dispatched = 0;

            try
            {
                dispatched = await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatch failed");
            }

            if (dispatched < BatchSize)
            {
                try
                {
                    await _signals.Outbox.WaitAsync(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RelayForgeDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IJobEventNotifier>();

        // A re-run after a dropped connection may push some events twice; clients dedupe by sequence.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var batch = await db.OutboxEvents
                .FromSql($"""
                    SELECT * FROM "OutboxEvents"
                    WHERE  "DispatchedAt" IS NULL
                    ORDER  BY "Id"
                    LIMIT  {BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                return 0;
            }

            foreach (var row in batch)
            {
                var evt = JsonSerializer.Deserialize<JobChangedEvent>(row.Payload, OutboxJson.Options)!;
                await notifier.NotifyAsync(evt, ct);
            }

            var ids = batch.Select(e => e.Id).ToList();
            await db.OutboxEvents
                .Where(e => ids.Contains(e.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.DispatchedAt, DateTimeOffset.UtcNow), ct);

            await tx.CommitAsync(ct);
            return batch.Count;
        }, cancellationToken);
    }
}
