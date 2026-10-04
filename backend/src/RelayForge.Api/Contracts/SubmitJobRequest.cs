namespace RelayForge.Api.Contracts;

public record SubmitJobRequest(
    string? Name,
    string? ScenarioKey,
    IReadOnlyList<SubmitJobTaskRequest> Tasks);

public record SubmitJobTaskRequest(
    string Key,
    string Name,
    int SimulatedDurationMs,
    IReadOnlyList<string> DependsOn,
    int? MaxAttempts = null,
    int? FailUntilAttempt = null);
