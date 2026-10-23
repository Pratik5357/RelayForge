namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// An in-process "something may have changed, look again" hint for a polling loop. Purely an
/// optimization: the database is the source of truth, and a lost or missed signal only costs
/// one poll interval of latency.
/// </summary>
public class Signal
{
    private TaskCompletionSource _next = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Wakes every waiter.</summary>
    public void Notify() =>
        Interlocked.Exchange(ref _next, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
            .TrySetResult();

    /// <summary>Waits until the next <see cref="Notify"/> or the timeout, whichever comes first.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await Volatile.Read(ref _next).Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            // Poll interval elapsed with no signal -- the caller polls the database anyway.
        }
    }
}

/// <summary>
/// The process's view of cross-process notifications. <see cref="PostgresListener"/> fills
/// these from Postgres NOTIFY, so a change committed by any process wakes every process.
/// </summary>
public class RelaySignals
{
    /// <summary>New Ready work (or a resumed job).</summary>
    public Signal Work { get; } = new();

    /// <summary>New outbox events to dispatch.</summary>
    public Signal Outbox { get; } = new();

    /// <summary>A job's cancellation was requested. Payload: job id.</summary>
    public event Action<Guid>? JobCancelRequested;

    /// <summary>A task wrote new log lines. Payload: job id, task id.</summary>
    public event Action<Guid, Guid>? LogsWritten;

    internal void RaiseJobCancelRequested(Guid jobId) => JobCancelRequested?.Invoke(jobId);

    internal void RaiseLogsWritten(Guid jobId, Guid taskId) => LogsWritten?.Invoke(jobId, taskId);
}

/// <summary>Postgres NOTIFY channel names.</summary>
public static class PgChannels
{
    public const string Ready = "relayforge_ready";
    public const string Outbox = "relayforge_outbox";
    public const string Cancel = "relayforge_cancel";
    public const string Logs = "relayforge_logs";
}
