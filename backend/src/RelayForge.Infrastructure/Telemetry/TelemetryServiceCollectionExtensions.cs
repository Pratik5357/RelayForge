using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RelayForge.Infrastructure.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Exports RelayForge's spans and metrics over OTLP when configured; does nothing otherwise
    /// (the Railway deployment has no collector).
    ///   Otel:TracesEndpoint  -- OTLP/gRPC, e.g. http://jaeger:4317
    ///   Otel:MetricsEndpoint -- OTLP/HTTP, e.g. http://prometheus:9090/api/v1/otlp/v1/metrics
    /// </summary>
    /// <param name="configureTracing">Host-specific instrumentation (the API adds ASP.NET Core).</param>
    public static IServiceCollection AddRelayForgeTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        var tracesEndpoint = configuration["Otel:TracesEndpoint"];
        var metricsEndpoint = configuration["Otel:MetricsEndpoint"];

        if (string.IsNullOrWhiteSpace(tracesEndpoint) && string.IsNullOrWhiteSpace(metricsEndpoint))
        {
            return services;
        }

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceInstanceId: Environment.MachineName));

        if (!string.IsNullOrWhiteSpace(tracesEndpoint))
        {
            otel.WithTracing(tracing =>
            {
                tracing
                    .AddSource(RelayForgeTelemetry.Name)
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(tracesEndpoint);
                        o.Protocol = OtlpExportProtocol.Grpc;
                    });
                configureTracing?.Invoke(tracing);
            });
        }

        if (!string.IsNullOrWhiteSpace(metricsEndpoint))
        {
            otel.WithMetrics(metrics => metrics
                .AddMeter(RelayForgeTelemetry.Name)
                .AddView("relayforge.task.queue_wait", new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000],
                })
                .AddView("relayforge.task.duration", new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [10, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000, 60000, 300000],
                })
                .AddOtlpExporter((o, reader) =>
                {
                    o.Endpoint = new Uri(metricsEndpoint);
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5000;
                }));
        }

        return services;
    }
}
