using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using RelayForge.Api.Endpoints;
using RelayForge.Api.Realtime;
using RelayForge.Infrastructure;
using RelayForge.Infrastructure.Execution;
using RelayForge.Infrastructure.Telemetry;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "FrontendDev";

builder.Services.AddRelayForgeCore(builder.Configuration);
builder.Services.AddRelayForgeOutboxDispatcher();

// One process runs everything by default (the Railway deployment). Set WorkerPool:RunInApi=false
// when RelayForge.Worker processes do the work (the Compose stack).
if (builder.Configuration.GetValue("WorkerPool:RunInApi", true))
{
    builder.Services.AddRelayForgeWorkers(builder.Configuration);
}

builder.Services.AddRelayForgeTelemetry(builder.Configuration, "relayforge-api", tracing =>
    tracing.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health")));

// Overrides the no-op notifier AddRelayForgeCore registered -- last registration of a service
// wins when resolved by interface, so this becomes the one the outbox dispatcher uses.
builder.Services.AddScoped<IJobEventNotifier, SignalRJobEventNotifier>();
builder.Services.AddHostedService<TaskLogRelay>();
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
app.MapGroup("/api/tasks").MapTaskEndpoints();
app.MapGroup("/api/schedules").MapScheduleEndpoints();
app.MapGroup("/api/handlers").MapHandlerEndpoints();
app.MapHub<JobEventsHub>("/hubs/jobs");

app.Run();

// Lets the integration tests boot this host with WebApplicationFactory<Program>.
public partial class Program;
