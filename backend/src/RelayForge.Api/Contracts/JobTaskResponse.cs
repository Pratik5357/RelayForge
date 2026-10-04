namespace RelayForge.Api.Contracts;

public record JobTaskResponse(
    Guid Id,
    string Name,
    string State,
    IReadOnlyList<Guid> DependsOn,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset? NextAttemptAt,
    Guid IdempotencyKey);
