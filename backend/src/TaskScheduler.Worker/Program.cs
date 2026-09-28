using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using TaskScheduler.Infrastructure;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog();
builder.Services.AddTaskScheduler(builder.Configuration, isWorkerProcess: true);

var otlp = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(otlp))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("relayforge-worker"))
        .WithTracing(t => t.AddHttpClientInstrumentation().AddSource("MassTransit").AddOtlpExporter());
}

var host = builder.Build();
await host.Services.EnsureSchedulerStoreAsync();
await host.RunAsync();
