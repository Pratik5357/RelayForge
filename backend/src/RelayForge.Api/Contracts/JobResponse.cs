namespace RelayForge.Api.Contracts;

public record JobSummaryResponse(
    Guid Id,
    string? Name,
    string? ScenarioKey,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int TaskCount);

public record JobDetailResponse(
    Guid Id,
    string? Name,
    string? ScenarioKey,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancellationRequestedAt,
    IReadOnlyList<JobTaskResponse> Tasks);
