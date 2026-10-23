namespace RelayForge.Infrastructure.Execution;

public class ReliabilityOptions
{
    public int BaseBackoffMs { get; set; } = 500;
    public int MaxBackoffMs { get; set; } = 8000;
    public int DefaultMaxAttempts { get; set; } = 4;

    /// <summary>
    /// How long a claim is good for without a heartbeat. Workers renew it every third of this,
    /// so a healthy task of any length keeps it and a dead worker's task is reclaimed within it.
    /// </summary>
    public int LeaseDurationMs { get; set; } = 15000;

    /// <summary>How often the sweep looks for expired leases.</summary>
    public int SweepIntervalMs { get; set; } = 1000;

    /// <summary>Per-attempt time limit for tasks that don't set their own.</summary>
    public int DefaultTimeoutMs { get; set; } = 300_000;
}

public class WorkerOptions
{
    /// <summary>Attempts one worker process runs at the same time.</summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>How often an idle worker checks for due work when no notification arrives.</summary>
    public int PollIntervalMs { get; set; } = 500;

    /// <summary>On shutdown, how long in-flight attempts may keep running before they're handed back.</summary>
    public int ShutdownGraceMs { get; set; } = 5000;

    /// <summary>How long a cancelled in-process handler gets to stop before the worker stops waiting for it.</summary>
    public int CancelGraceMs { get; set; } = 2000;
}

public class FairnessOptions
{
    /// <summary>A waiting task's effective priority rises by 1 per this many seconds, so nothing starves.</summary>
    public int AgingSeconds { get; set; } = 60;

    /// <summary>Soft cap on Running tasks per tenant; 0 = no cap.</summary>
    public int MaxRunningPerTenant { get; set; }
}

public class HarnessOptions
{
    /// <summary>Path to RelayForge.Runner.dll. Defaults to the one next to the worker.</summary>
    public string? RunnerPath { get; set; }

    /// <summary>Image for Container isolation; must contain RelayForge.Runner.dll in /app.</summary>
    public string ContainerImage { get; set; } = "relayforge-runner";
    public int ContainerMemoryMb { get; set; } = 256;
    public double ContainerCpus { get; set; } = 0.5;

    /// <summary>Most log lines stored per attempt; the rest are dropped with a note.</summary>
    public int MaxLogLinesPerAttempt { get; set; } = 1000;
}
