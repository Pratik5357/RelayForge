using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RelayForge.Infrastructure.Telemetry;

/// <summary>
/// RelayForge's own spans and metrics. Exporting them is the host's job (see AddRelayForgeTelemetry);
/// with no exporter configured these cost next to nothing.
/// </summary>
public static class RelayForgeTelemetry
{
    public const string Name = "RelayForge";

    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);

    /// <summary>Time from Ready (or due) to claimed. The best single health signal: it grows when workers can't keep up.</summary>
    public static readonly Histogram<double> QueueWait =
        Meter.CreateHistogram<double>("relayforge.task.queue_wait", "ms", "Time a task waited between becoming due and being claimed.");

    public static readonly Histogram<double> Duration =
        Meter.CreateHistogram<double>("relayforge.task.duration", "ms", "Execution time of one attempt.");

    public static readonly Counter<long> Attempts =
        Meter.CreateCounter<long>("relayforge.task.attempts", description: "Finished attempts, by outcome.");

    public static readonly Counter<long> Retries =
        Meter.CreateCounter<long>("relayforge.task.retries", description: "Attempts that failed and were scheduled to retry.");

    public static readonly Counter<long> DeadLettered =
        Meter.CreateCounter<long>("relayforge.task.dead_lettered", description: "Tasks that ran out of attempts. Work that needs a human.");

    public static readonly Counter<long> LeasesReclaimed =
        Meter.CreateCounter<long>("relayforge.lease.reclaimed", description: "Attempts reclaimed after their worker stopped heartbeating.");

    public static readonly Counter<long> StaleResultsRejected =
        Meter.CreateCounter<long>("relayforge.lease.stale_results_rejected", description: "Late results from workers that had lost their lease.");

    public static readonly Counter<long> JobsSubmitted =
        Meter.CreateCounter<long>("relayforge.jobs.submitted", description: "Jobs created, by source (api, schedule).");

    private static long _readyTasks;
    private static long _runningTasks;

    /// <summary>Queue depth. Rising steadily means more workers are needed.</summary>
    public static readonly ObservableGauge<long> ReadyTasks =
        Meter.CreateObservableGauge("relayforge.tasks.ready", () => Interlocked.Read(ref _readyTasks), description: "Tasks waiting to be claimed.");

    public static readonly ObservableGauge<long> RunningTasks =
        Meter.CreateObservableGauge("relayforge.tasks.running", () => Interlocked.Read(ref _runningTasks), description: "Tasks being executed.");

    /// <summary>Updated by the sweep leader each tick from one cheap count query.</summary>
    public static void RecordQueueDepth(long ready, long running)
    {
        Interlocked.Exchange(ref _readyTasks, ready);
        Interlocked.Exchange(ref _runningTasks, running);
    }
}
