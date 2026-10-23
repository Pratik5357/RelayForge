using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RelayForge.Domain.Enums;
using RelayForge.Infrastructure.Telemetry;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// Reclaims Running tasks whose lease has expired -- the worker crashed, restarted or froze
/// mid-attempt -- once on becoming leader and then every Reliability:SweepIntervalMs. Runs on
/// one worker process at a time (see <see cref="LeaderService"/>); a second sweep would be
/// harmless, because reclaim re-checks the lease under the job lock.
///
/// Retries need no sweep: a retry is a Ready row with a future NextAttemptAt, and the workers'
/// claim query skips it until it's due. The leader also samples queue depth for the
/// relayforge.tasks.ready/running gauges.
/// </summary>
public class ReliabilitySweepHostedService : LeaderService
{
    public const long LockKey = 727_001;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReliabilityOptions _options;
    private int _tick;

    public ReliabilitySweepHostedService(
        IServiceScopeFactory scopeFactory,
        RelayForgeConnectionString connectionString,
        IOptions<ReliabilityOptions> options,
        ILogger<ReliabilitySweepHostedService> logger)
        : base(connectionString, LockKey, logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    protected override TimeSpan Interval => TimeSpan.FromMilliseconds(_options.SweepIntervalMs);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TaskAttemptService>().ReclaimExpiredAsync(cancellationToken);

        if (_tick++ % 5 == 0)
        {
            var db = scope.ServiceProvider.GetRequiredService<RelayForgeDbContext>();
            var counts = await db.JobTasks
                .Where(t => t.State == TaskState.Ready || t.State == TaskState.Running)
                .GroupBy(t => t.State)
                .Select(g => new { State = g.Key, Count = g.LongCount() })
                .ToListAsync(cancellationToken);

            RelayForgeTelemetry.RecordQueueDepth(
                counts.FirstOrDefault(c => c.State == TaskState.Ready)?.Count ?? 0,
                counts.FirstOrDefault(c => c.State == TaskState.Running)?.Count ?? 0);
        }
    }
}
