using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RelayForge.Infrastructure.Execution;

namespace RelayForge.Infrastructure.Scheduling;

/// <summary>
/// Fires due schedules once a second, on one worker process at a time (leader-elected). A crash
/// between creating a run and moving the schedule's NextFireAt is harmless: the next tick sees
/// the same due time again, and the unique (DefinitionId, ScheduledFor) index turns the second
/// insert into a no-op. Correctness rests on that constraint, not on the leader lock.
/// </summary>
public class SchedulerHostedService : LeaderService
{
    public const long LockKey = 727_002;

    private readonly IServiceScopeFactory _scopeFactory;
    private bool _seeded;

    public SchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        RelayForgeConnectionString connectionString,
        ILogger<SchedulerHostedService> logger)
        : base(connectionString, LockKey, logger)
    {
        _scopeFactory = scopeFactory;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(1);

    protected override async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var schedules = scope.ServiceProvider.GetRequiredService<ScheduleService>();

        if (!_seeded)
        {
            await schedules.EnsureSystemSchedulesAsync(cancellationToken);
            _seeded = true;
        }

        await schedules.FireDueAsync(DateTimeOffset.UtcNow, cancellationToken);
    }
}
