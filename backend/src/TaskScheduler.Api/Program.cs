using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;
using TaskScheduler.Api.Realtime;
using TaskScheduler.Infrastructure;
using TaskScheduler.Infrastructure.Events;
using TaskScheduler.Infrastructure.Insights;
using TaskScheduler.Infrastructure.Jobs;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowCredentials().SetIsOriginAllowed(_ => true)));
builder.Services.AddSignalR();
builder.Services.AddSingleton<IJobEventPublisher, SignalRJobEventPublisher>();
builder.Services.AddTaskScheduler(builder.Configuration, isWorkerProcess: false);

var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("relayforge-api"))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddHttpClientInstrumentation();
        t.AddSource("MassTransit");
        if (!string.IsNullOrWhiteSpace(otlp))
        {
            t.AddOtlpExporter();
        }
    });

var app = builder.Build();

await app.Services.EnsureSchedulerStoreAsync();

app.UseSerilogRequestLogging();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpMetrics();

app.MapPost("/api/jobs", async (SubmitJobRequest request, JobService jobs, CancellationToken ct) =>
{
    try
    {
        var created = await jobs.SubmitAsync(request, ct);
        return Results.Created($"/api/jobs/{created.Id}", created);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (TaskScheduler.Domain.DagCycleException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/jobs", async (JobService jobs, int? take, CancellationToken ct) =>
    Results.Ok(await jobs.ListAsync(take ?? 50, ct)));

app.MapGet("/api/jobs/{id:guid}", async (Guid id, JobService jobs, CancellationToken ct) =>
{
    var job = await jobs.GetAsync(id, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapPost("/api/jobs/{id:guid}/cancel", async (Guid id, JobService jobs, CancellationToken ct) =>
{
    var job = await jobs.CancelAsync(id, ct);
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapGet("/api/jobs/{id:guid}/tasks", async (Guid id, JobService jobs, CancellationToken ct) =>
{
    var job = await jobs.GetAsync(id, ct);
    return job is null ? Results.NotFound() : Results.Ok(job.Tasks);
});

app.MapGet("/api/system", async (SystemInfoService system, CancellationToken ct) =>
    Results.Ok(await system.GetAsync(ct)));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<JobEventsHub>("/hubs/jobs");
app.MapMetrics();

app.Run();

public partial class Program;
