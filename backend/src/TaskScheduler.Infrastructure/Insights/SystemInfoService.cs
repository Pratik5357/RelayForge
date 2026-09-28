using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace TaskScheduler.Infrastructure.Insights;

/// <summary>How this instance is wired, so the dashboard can say which phase of the build is running.</summary>
public sealed record WiringDto(
    string Database,
    string Queue,
    string LockProvider,
    int WorkerCount,
    bool InProcessWorkers,
    double LeaseSeconds,
    double HeartbeatSeconds);

public sealed record CountsDto(
    int Jobs,
    int Tasks,
    int DeadLetters,
    int LiveWorkers,
    IReadOnlyDictionary<string, int> JobsByState,
    IReadOnlyDictionary<string, int> TasksByState);

public sealed record SystemInfoDto(WiringDto Wiring, CountsDto Counts, DateTimeOffset ServerTime);

/// <summary>
/// Read-only snapshot for the dashboard's "how this instance is wired" panel. It
/// exists so a visitor can tell whether they are looking at the single-process
/// SQLite setup or the full Postgres/RabbitMQ/Redis stack.
/// </summary>
public sealed class SystemInfoService
{
    private readonly SchedulerDbContext _db;
    private readonly SchedulerOptions _options;

    public SystemInfoService(SchedulerDbContext db, IOptions<SchedulerOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<SystemInfoDto> GetAsync(CancellationToken cancellationToken)
    {
        // A worker that has missed three heartbeats is treated as gone, the same
        // window the lease reaper effectively works with.
        var cutoff = DateTimeOffset.UtcNow - (_options.HeartbeatInterval * 3);

        var jobsByState = await _db.Jobs
            .AsNoTracking()
            .GroupBy(j => j.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var tasksByState = await _db.JobTasks
            .AsNoTracking()
            .GroupBy(t => t.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // SQLite cannot compare DateTimeOffset in SQL, so the freshness check runs
        // in memory — same trick as LeaseReaperHostedService.
        var heartbeats = await _db.WorkerHeartbeats.AsNoTracking().ToListAsync(cancellationToken);

        var counts = new CountsDto(
            Jobs: jobsByState.Sum(x => x.Count),
            Tasks: tasksByState.Sum(x => x.Count),
            DeadLetters: await _db.DeadLetters.AsNoTracking().CountAsync(cancellationToken),
            LiveWorkers: heartbeats.Count(w => w.LastSeenAt >= cutoff),
            JobsByState: jobsByState.ToDictionary(x => x.State.ToString(), x => x.Count),
            TasksByState: tasksByState.ToDictionary(x => x.State.ToString(), x => x.Count));

        var wiring = new WiringDto(
            Database: _options.Database,
            Queue: _options.Queue,
            LockProvider: _options.LockProvider,
            WorkerCount: _options.WorkerCount,
            InProcessWorkers: _options.RunInProcessWorkers,
            LeaseSeconds: _options.LeaseDuration.TotalSeconds,
            HeartbeatSeconds: _options.HeartbeatInterval.TotalSeconds);

        return new SystemInfoDto(wiring, counts, DateTimeOffset.UtcNow);
    }
}
