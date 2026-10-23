namespace RelayForge.Domain.Entities;

/// <summary>
/// A job event written in the same transaction as the state change it describes, then pushed
/// to live clients by a dispatcher. Sequence numbers are per job, gap-free and increasing, so a
/// client can drop duplicates and notice what it missed.
/// </summary>
public class OutboxEvent
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public int Sequence { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}
