namespace TaskScheduler.Dashboard.Api;

public sealed record TaskStatusDto(
    Guid Id,
    string Key,
    string Type,
    string State,
    int AttemptCount,
    int MaxAttempts,
    string? LastError,
    string? ResultJson,
    IReadOnlyList<string> DependsOn);

public sealed record JobStatusDto(
    Guid Id,
    string Name,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<TaskStatusDto> Tasks);
