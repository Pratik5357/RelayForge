using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RelayForge.Infrastructure.Execution;

namespace RelayForge.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRelayForgeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Railway (and Heroku-style hosts) expose DATABASE_URL as postgres://user:pass@host:port/db.
            connectionString = FromDatabaseUrl(configuration["DATABASE_URL"]);
        }
        connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new InvalidOperationException(
                "Missing ConnectionStrings:Default. Set it via `dotnet user-secrets set \"ConnectionStrings:Default\" \"...\"` in RelayForge.Api, or provide DATABASE_URL.")
            : connectionString;

        services.AddDbContext<RelayForgeDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        services.Configure<ReliabilityOptions>(configuration.GetSection("Reliability"));

        services.AddSingleton<InProcessTaskQueue>();
        services.AddScoped<SimulatedTaskExecutor>();
        services.AddScoped<TaskFailureHandler>();
        services.AddScoped<JobOrchestrator>();

        // Default no-op; the composition root (RelayForge.Api) registers the real
        // SignalR-backed notifier after calling this method, which overrides this registration.
        services.AddScoped<IJobEventNotifier, NullJobEventNotifier>();

        services.AddHostedService<WorkerPoolHostedService>();
        services.AddHostedService<ReliabilitySweepHostedService>();

        return services;
    }

    private static string? FromDatabaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        return new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            SslMode = Npgsql.SslMode.Prefer,
        }.ConnectionString;
    }
}
