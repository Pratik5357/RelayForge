using System.Collections.Concurrent;

namespace RelayForge.Infrastructure.Execution;

public enum StopReason
{
    None,
    Timeout,
    JobCancelled,
    LeaseLost,
    Shutdown,
}

/// <summary>
/// The cancellation token one attempt's handler sees, plus the first reason it was cancelled,
/// so the worker can tell a timeout (retry) from a cancel (Cancelled) from a lost lease (stop,
/// write nothing) from a shutdown (hand back).
/// </summary>
public sealed class AttemptControl : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly CancellationTokenRegistration _shutdown;
    private readonly Timer _timeout;
    private int _reason;

    public AttemptControl(Guid jobId, TimeSpan timeout, CancellationToken shutdown)
    {
        JobId = jobId;
        _cts = new CancellationTokenSource();
        _shutdown = shutdown.Register(() => Stop(StopReason.Shutdown));
        _timeout = new Timer(_ => Stop(StopReason.Timeout), null, timeout, Timeout.InfiniteTimeSpan);
    }

    public Guid JobId { get; }

    public CancellationToken Token => _cts.Token;

    public StopReason Reason => (StopReason)Volatile.Read(ref _reason);

    public void Stop(StopReason reason)
    {
        if (Interlocked.CompareExchange(ref _reason, (int)reason, (int)StopReason.None) == (int)StopReason.None)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public void Dispose()
    {
        _timeout.Dispose();
        _shutdown.Dispose();
        _cts.Dispose();
    }
}

/// <summary>
/// Attempts running in this process, by job, so a cancel NOTIFY can stop them immediately
/// instead of waiting for the next heartbeat.
/// </summary>
public sealed class RunningAttempts
{
    private readonly ConcurrentDictionary<AttemptControl, byte> _running = new();

    public RunningAttempts(RelaySignals signals)
    {
        signals.JobCancelRequested += CancelJob;
    }

    public int Count => _running.Count;

    public IDisposable Track(AttemptControl control)
    {
        _running[control] = 0;
        return new Untrack(this, control);
    }

    public void CancelJob(Guid jobId)
    {
        foreach (var control in _running.Keys.Where(c => c.JobId == jobId))
        {
            control.Stop(StopReason.JobCancelled);
        }
    }

    private sealed class Untrack(RunningAttempts owner, AttemptControl control) : IDisposable
    {
        public void Dispose() => owner._running.TryRemove(control, out _);
    }
}
