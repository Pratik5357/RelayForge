using Prometheus;

namespace TaskScheduler.Infrastructure;

public static class SchedulerMetrics
{
    public static readonly Counter Executed = Metrics.CreateCounter(
        "relayforge_tasks_executed_total", "Tasks that finished execution (success or failure).");

    public static readonly Counter Failed = Metrics.CreateCounter(
        "relayforge_tasks_failed_total", "Task attempts that failed.");

    public static readonly Counter Succeeded = Metrics.CreateCounter(
        "relayforge_tasks_succeeded_total", "Tasks that succeeded.");

    public static readonly Counter DeadLettered = Metrics.CreateCounter(
        "relayforge_tasks_dead_lettered_total", "Tasks moved to the dead-letter table.");

    public static readonly Counter LeaseReclaims = Metrics.CreateCounter(
        "relayforge_lease_reclaims_total", "Tasks reclaimed after lease expiry.");

    public static readonly Counter Enqueued = Metrics.CreateCounter(
        "relayforge_tasks_enqueued_total", "Task-ready messages written to the queue.");

    public static readonly Histogram Duration = Metrics.CreateHistogram(
        "relayforge_task_duration_seconds", "Handler wall-clock time.");

    public static readonly Gauge QueueDepth = Metrics.CreateGauge(
        "relayforge_queue_depth", "In-process channel depth (0 when using RabbitMQ).");
}
