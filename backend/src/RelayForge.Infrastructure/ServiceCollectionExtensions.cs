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
