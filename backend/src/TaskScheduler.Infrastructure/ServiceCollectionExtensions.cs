using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaskScheduler.Domain;
using TaskScheduler.Infrastructure.Events;
using TaskScheduler.Infrastructure.Execution;
using TaskScheduler.Infrastructure.Handlers;
using TaskScheduler.Infrastructure.Hosting;
using TaskScheduler.Infrastructure.Insights;
using TaskScheduler.Infrastructure.Jobs;
using TaskScheduler.Infrastructure.Locking;
using TaskScheduler.Infrastructure.Queueing;

namespace TaskScheduler.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskScheduler(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isWorkerProcess = false)
    {
        services.Configure<SchedulerOptions>(configuration.GetSection(SchedulerOptions.SectionName));
        var options = new SchedulerOptions();
        configuration.GetSection(SchedulerOptions.SectionName).Bind(options);

        var connectionString = configuration.GetConnectionString("Scheduler")
                               ?? "Data Source=relayforge.db";

        services.AddDbContext<SchedulerDbContext>(db =>
        {
            if (string.Equals(options.Database, "Postgres", StringComparison.OrdinalIgnoreCase))
            {
                db.UseNpgsql(connectionString);
            }
            else
            {
                db.UseSqlite(connectionString);
                db.AddInterceptors(new SqlitePragmaInterceptor());
            }
        });

        services.AddSingleton(new ExponentialBackoffRetryPolicy());
        services.AddScoped<TaskExecutor>();
        services.AddScoped<JobService>();
        services.AddScoped<SystemInfoService>();
        services.AddSingleton<ITaskHandler, EchoHandler>();
        services.AddSingleton<ITaskHandler, DelayHandler>();
        services.AddSingleton<ITaskHandler, FailHandler>();

        if (!services.Any(d => d.ServiceType == typeof(IJobEventPublisher)))
        {
            services.AddSingleton<IJobEventPublisher, NoOpJobEventPublisher>();
        }

        var useRabbit = string.Equals(options.Queue, "RabbitMq", StringComparison.OrdinalIgnoreCase);
        if (useRabbit)
        {
            services.AddMassTransit(x =>
            {
                x.AddConsumer<TaskReadyConsumer>();
                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(configuration.GetConnectionString("RabbitMq") ?? "amqp://guest:guest@localhost:5672");
                    if (isWorkerProcess)
                    {
                        cfg.ReceiveEndpoint("relayforge-task-ready", e =>
                        {
                            e.PrefetchCount = options.WorkerCount;
                            e.ConfigureConsumer<TaskReadyConsumer>(context);
                        });
                    }
                });
            });
            services.AddScoped<ITaskQueue, MassTransitTaskQueue>();
        }
        else
        {
            services.AddSingleton<ChannelTaskQueue>();
            services.AddSingleton<ITaskQueue>(sp => sp.GetRequiredService<ChannelTaskQueue>());
        }

        if (string.Equals(options.LockProvider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ITaskLock>(_ =>
                new RedisTaskLock(configuration.GetConnectionString("Redis") ?? "localhost:6379"));
        }
        else
        {
            services.AddSingleton<ITaskLock, InMemoryTaskLock>();
        }

        services.AddHostedService<HeartbeatHostedService>();
        services.AddHostedService<LeaseReaperHostedService>();
        services.AddHostedService<RetryDispatcherHostedService>();

        var runChannelWorkers = !useRabbit && (options.RunInProcessWorkers || isWorkerProcess);
        if (runChannelWorkers)
        {
            services.AddHostedService<ChannelWorkerHostedService>();
        }

        return services;
    }

    public static async Task EnsureSchedulerStoreAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        await db.Database.EnsureCreatedAsync(cancellationToken);
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
            await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);
        }
    }
}
