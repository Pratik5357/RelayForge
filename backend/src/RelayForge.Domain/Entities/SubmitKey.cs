namespace RelayForge.Domain.Entities;

/// <summary>
/// Remembers an Idempotency-Key sent with POST /api/jobs: resending the same key and body
/// returns the original job instead of creating a second one.
/// </summary>
public class SubmitKey
{
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public Guid JobId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
