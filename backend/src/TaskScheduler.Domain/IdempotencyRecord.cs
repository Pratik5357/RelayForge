namespace TaskScheduler.Domain;

public sealed class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public Guid JobTaskId { get; set; }
    public string ResultJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
