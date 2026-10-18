using Microsoft.EntityFrameworkCore;
using RelayForge.Api.Endpoints;
using RelayForge.Api.Realtime;
using RelayForge.Infrastructure;
using RelayForge.Infrastructure.Execution;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "FrontendDev";

builder.Services.AddRelayForgeInfrastructure(builder.Configuration);

// Overrides the no-op notifier AddRelayForgeInfrastructure registered -- last registration
// of a service wins when resolved by interface, so this becomes the one that's actually used.
builder.Services.AddScoped<IJobEventNotifier, SignalRJobEventNotifier>();
builder.Services.AddSignalR();

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        // SignalR needs AllowCredentials(), which the CORS spec forbids combining with a
        // wildcard origin -- WithOrigins (an explicit origin) is required here.
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        policy.WithOrigins(origins is { Length: > 0 } ? origins : new[] { "http://localhost:3000" })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Opt-in: set Database__MigrateOnStartup=true to apply pending EF migrations at boot.
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<RelayForgeDbContext>().Database.Migrate();
}

app.UseCors(FrontendCorsPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Runs a real query, unlike /health, so it confirms the API can actually reach the database.
app.MapGet("/health/db", async (RelayForgeDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        var hasJobs = await db.Jobs.AnyAsync(cancellationToken);
        return Results.Ok(new { status = "ok", database = "reachable", hasJobs });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        return Results.Problem(detail: "Database query failed.", statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unavailable");
    }
});

app.MapGroup("/api/jobs").MapJobEndpoints();
app.MapHub<JobEventsHub>("/hubs/jobs");

app.Run();
