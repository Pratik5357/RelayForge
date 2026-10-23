using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RelayForge.Handlers;
using RelayForge.Infrastructure.Execution;
using RelayForge.Infrastructure.Harness;
using RelayForge.Infrastructure.Jobs;
using RelayForge.Infrastructure.Scheduling;

namespace RelayForge.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// What every RelayForge host needs: the database, job submission and operator actions,
    /// task handler metadata (for validation), and the NOTIFY listener.
    /// </summary>
    public static IServiceCollection AddRelayForgeCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ConnectionStrings:Default may be a key=value string or a postgres:// URL (as Supabase
        // copies it); both go through FromDatabaseUrl, which passes key=value strings unchanged.
        var connectionString = FromDatabaseUrl(configuration.GetConnectionString("Default"));
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Railway (and Heroku-style hosts) expose DATABASE_URL as postgres://user:pass@host:port/db.
            connectionString = FromDatabaseUrl(configuration["DATABASE_URL"]);
        }
        connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                "Missing ConnectionStrings:Default. Set it via `dotnet user-secrets set \"ConnectionStrings:Default\" \"...\"` in RelayForge.Api, or provide DATABASE_URL.")
            : connectionString;

        services.AddSingleton(new RelayForgeConnectionString(connectionString));
        services.AddDbContext<RelayForgeDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        services.Configure<ReliabilityOptions>(configuration.GetSection("Reliability"));
        services.Configure<WorkerOptions>(configuration.GetSection("WorkerPool"));
        services.Configure<FairnessOptions>(configuration.GetSection("Fairness"));
        services.Configure<HarnessOptions>(configuration.GetSection("Harness"));

        services.AddBuiltInTaskHandlers();
        services.AddTaskHandler<RetentionHandler>();

        services.AddSingleton<RelaySignals>();
        services.AddScoped<JobOrchestrator>();
        services.AddScoped<TaskFailureHandler>();
        services.AddScoped<TaskAttemptService>();
        services.AddScoped<JobSubmissionService>();
        services.AddScoped<JobOperations>();
        services.AddScoped<ScheduleService>();

        // Default no-op; the API registers the real SignalR-backed notifier after this call.
        services.AddScoped<IJobEventNotifier, NullJobEventNotifier>();

        services.AddHostedService<PostgresListener>();

        return services;
    }

    /// <summary>
    /// Task execution: the worker pool, the lease-reclaim sweep and the scheduler (both
    /// leader-elected), and the execution harness. Hosted by RelayForge.Worker, and by the API
    /// too when WorkerPool:RunInApi is true (the single-process deployment).
    /// </summary>
    public static IServiceCollection AddRelayForgeWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<RunningAttempts>();
        services.AddSingleton<InProcessExecutor>();
        services.AddSingleton<ProcessExecutor>();
        services.AddSingleton<TaskExecutorSelector>();
        services.AddScoped<AttemptRunner>();

        services.AddHostedService<WorkerPoolHostedService>();
        services.AddHostedService<ReliabilitySweepHostedService>();
        services.AddHostedService<SchedulerHostedService>();

        // Give in-flight attempts their grace period, plus time to report, before the host
        // gives up on stopping.
        var worker = configuration.GetSection("WorkerPool").Get<WorkerOptions>() ?? new WorkerOptions();
        services.Configure<HostOptions>(o =>
            o.ShutdownTimeout = TimeSpan.FromMilliseconds(worker.ShutdownGraceMs + worker.CancelGraceMs) + TimeSpan.FromSeconds(10));

        return services;
    }

    /// <summary>Delivers outbox events to live clients. Hosted by the API.</summary>
    public static IServiceCollection AddRelayForgeOutboxDispatcher(this IServiceCollection services)
    {
        services.AddHostedService<OutboxDispatcherHostedService>();
        return services;
    }

    // Parses postgres://user:pass@host:port/db by hand: System.Uri rejects raw '@', '#', '/' or
    // brackets in the password, which is easy to hit with copy-pasted Supabase strings.
    // A plain key=value Npgsql connection string is passed through unchanged.
    private static string? FromDatabaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim().Trim('"');

        const string pg = "postgres://", pgql = "postgresql://";
        string rest;
        if (url.StartsWith(pg, StringComparison.OrdinalIgnoreCase)) rest = url[pg.Length..];
        else if (url.StartsWith(pgql, StringComparison.OrdinalIgnoreCase)) rest = url[pgql.Length..];
        else return url;

        var at = rest.LastIndexOf('@');
        if (at < 0) throw new InvalidOperationException("DATABASE_URL is missing 'user:password@host'.");
        var userInfo = rest[..at];
        var hostPart = rest[(at + 1)..];

        var colon = userInfo.IndexOf(':');
        var user = colon < 0 ? userInfo : userInfo[..colon];
        var password = colon < 0 ? null : userInfo[(colon + 1)..];
        if (password is not null && password.StartsWith('[') && password.EndsWith(']'))
            throw new InvalidOperationException(
                "DATABASE_URL still contains the [YOUR-PASSWORD] placeholder; replace it, brackets included, with the real password.");

        var slash = hostPart.IndexOf('/');
        var hostPort = slash < 0 ? hostPart : hostPart[..slash];
        var db = slash < 0 ? "postgres" : hostPart[(slash + 1)..].Split('?')[0];
        var hp = hostPort.Split(':', 2);

        return new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = hp[0],
            Port = hp.Length > 1 && int.TryParse(hp[1], out var port) ? port : 5432,
            Database = db,
            Username = Uri.UnescapeDataString(user),
            Password = password is null ? null : Uri.UnescapeDataString(password),
            SslMode = Npgsql.SslMode.Require,
        }.ConnectionString;
    }
}
