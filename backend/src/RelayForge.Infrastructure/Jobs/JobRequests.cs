using System.Text.Json;

namespace RelayForge.Infrastructure.Jobs;

/// <summary>
/// A job to create: what POST /api/jobs takes, and what a schedule stores as its template.
/// </summary>
/// <param name="Priority">Default priority for every task; higher runs first.</param>
/// <param name="TenantId">Optional owner; Fairness:MaxRunningPerTenant caps its running tasks.</param>
public record SubmitJobRequest(
    string? Name,
    string? ScenarioKey,
    IReadOnlyList<SubmitJobTaskRequest> Tasks,
    int Priority = 0,
    string? TenantId = null);

/// <param name="Type">Handler type; defaults to "simulated".</param>
/// <param name="Payload">The handler's input. For "simulated" it may be left out, and is then
/// built from SimulatedDurationMs and FailUntilAttempt (the v1 request shape).</param>
/// <param name="Isolation">InProcess (default), Process or Container.</param>
/// <param name="TimeoutMs">Per-attempt time limit; defaults to Reliability:DefaultTimeoutMs.</param>
/// <param name="Priority">Overrides the job's priority for this task.</param>
public record SubmitJobTaskRequest(
    string Key,
    string Name,
    int SimulatedDurationMs = 0,
    IReadOnlyList<string>? DependsOn = null,
    int? MaxAttempts = null,
    int? FailUntilAttempt = null,
    string? Type = null,
    JsonElement? Payload = null,
    string? Isolation = null,
    int? TimeoutMs = null,
    int? Priority = null);

/// <summary>Where a submission came from, and the guarantees it asks for.</summary>
/// <param name="IdempotencyKey">Same key + same body returns the first job; same key + different body is refused.</param>
/// <param name="DefinitionId">The schedule creating this run (with ScheduledFor: unique together).</param>
public record SubmitContext(
    string Source,
    string? IdempotencyKey = null,
    Guid? DefinitionId = null,
    DateTimeOffset? ScheduledFor = null)
{
    public static readonly SubmitContext Api = new("api");
}

/// <param name="Created">False when an idempotent resubmit or a duplicate scheduled run returned an existing job.</param>
public record SubmitResult(Guid JobId, bool Created);

/// <summary>The request can't be accepted as given (400).</summary>
public sealed class JobValidationException(string title, string message) : Exception(message)
{
    public string Title { get; } = title;
}

/// <summary>An Idempotency-Key was reused with a different request body (422).</summary>
public sealed class IdempotencyConflictException(string key)
    : Exception($"Idempotency-Key '{key}' was already used with a different request body.");
