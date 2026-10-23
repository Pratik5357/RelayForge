namespace RelayForge.Domain.Enums;

/// <summary>What to do about scheduled times that passed while the scheduler was down.</summary>
public enum MisfirePolicy
{
    /// <summary>Run once now for all of them. Good for daily reports.</summary>
    FireOnce,

    /// <summary>Drop them and wait for the next time. Good when a late run is useless.</summary>
    Skip,

    /// <summary>Run every missed time (capped). Good when each run processes its own window.</summary>
    CatchUp,
}

/// <summary>What to do when a run is due while the previous one is still going.</summary>
public enum ConcurrencyPolicy
{
    Allow,

    /// <summary>Skip this run.</summary>
    Forbid,

    /// <summary>Cancel the running one and start this one.</summary>
    Replace,
}
