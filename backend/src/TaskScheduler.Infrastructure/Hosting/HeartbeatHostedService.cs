using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TaskScheduler.Domain;

namespace TaskScheduler.Infrastructure.Hosting;

public sealed class HeartbeatHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SchedulerOptions _options;

    public HeartbeatHostedService(IServiceScopeFactory scopeFactory, IOptions<SchedulerOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
            var existing = await db.WorkerHeartbeats
                .FirstOrDefaultAsync(w => w.WorkerId == _options.WorkerId, stoppingToken);
            if (existing is null)
            {
                db.WorkerHeartbeats.Add(new WorkerHeartbeat
                {
                    WorkerId = _options.WorkerId,
                    HostName = Environment.MachineName,
                    LastSeenAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existing.LastSeenAt = DateTimeOffset.UtcNow;
                existing.HostName = Environment.MachineName;
            }

            await db.SaveChangesAsync(stoppingToken);
            await Task.Delay(_options.HeartbeatInterval, stoppingToken);
        }
    }
}
