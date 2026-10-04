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
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Missing ConnectionStrings:Default. Set it via `dotnet user-secrets set \"ConnectionStrings:Default\" \"...\"` in RelayForge.Api.");

        services.AddDbContext<RelayForgeDbContext>(options =>
            options.UseSqlServer(connectionString));

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
}
