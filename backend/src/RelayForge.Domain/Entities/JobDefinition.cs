using RelayForge.Domain.Enums;

namespace RelayForge.Domain.Entities;

/// <summary>A recurring job: a DAG template submitted on a cron schedule.</summary>
public class JobDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Standard 5-field cron, e.g. "0 2 * * *".</summary>
    public string Cron { get; set; } = string.Empty;

    /// <summary>IANA zone the cron is read in, e.g. "Asia/Kolkata". Fire times are stored in UTC.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>The job to submit each time, as the same JSON POST /api/jobs takes.</summary>
    public string Template { get; set; } = "{}";

    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.FireOnce;
    public ConcurrencyPolicy ConcurrencyPolicy { get; set; } = ConcurrencyPolicy.Allow;

    public DateTimeOffset NextFireAt { get; set; }
    public DateTimeOffset? LastFiredAt { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Created by RelayForge itself (e.g. retention); can be paused but not deleted.</summary>
    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
