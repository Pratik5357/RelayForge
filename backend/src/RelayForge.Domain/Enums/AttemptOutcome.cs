namespace RelayForge.Domain.Enums;

public enum AttemptOutcome
{
    Succeeded,

    /// <summary>Failed in a way worth retrying (an error, a timeout).</summary>
    Failed,

    /// <summary>Failed in a way retrying can't fix; the task ends Failed.</summary>
    Fatal,

    /// <summary>The worker stopped renewing its lease (crashed, froze) and the attempt was reclaimed.</summary>
    LeaseLost,

    /// <summary>Stopped because the job was cancelled.</summary>
    Cancelled,

    /// <summary>Handed back on a graceful worker shutdown; doesn't count against the retry budget.</summary>
    Released,
}
