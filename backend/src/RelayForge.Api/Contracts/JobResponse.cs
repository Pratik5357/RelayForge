using System.Text.Json;

namespace RelayForge.Api.Contracts;

public record JobSummaryResponse(
    Guid Id,
    string? Name,
    string? ScenarioKey,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int TaskCount,
    DateTimeOffset? PausedAt = null,
    Guid? DefinitionId = null,
    DateTimeOffset? ScheduledFor = null);

/// <param name="LastEventSequence">Sequence of the newest event, so a client knows where its event stream should resume.</param>
public record JobDetailResponse(
    Guid Id,
    string? Name,
    string? ScenarioKey,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancellationRequestedAt,
    IReadOnlyList<JobTaskResponse> Tasks,
    DateTimeOffset? PausedAt,
    string? TenantId,
    Guid? DefinitionId,
    DateTimeOffset? ScheduledFor,
    int LastEventSequence,
    InvariantsResponse Invariants);

/// <summary>
/// What the run's attempt history proves. The guarantee panels in the demo read these:
/// duplicate completions must stay 0, however many workers crash or freeze.
/// </summary>
/// <param name="DuplicateCompletions">Tasks with more than one successful attempt.</param>
/// <param name="StaleResultsRejected">Late results from workers that had lost their lease.</param>
public record InvariantsResponse(
    int Attempts,
    int SucceededAttempts,
    int DuplicateCompletions,
    int LeasesReclaimed,
    int StaleResultsRejected);

public record TaskAttemptResponse(
    int Attempt,
    string Worker,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? Outcome,
    string? Error,
    string? RejectedLateResult);

public record TaskLogResponse(int Attempt, int Sequence, string Stream, string Line, DateTimeOffset At);

public record ScheduleResponse(
    Guid Id,
    string Name,
    string Cron,
    string TimeZone,
    string MisfirePolicy,
    string ConcurrencyPolicy,
    bool Enabled,
    bool IsSystem,
    DateTimeOffset NextFireAt,
    DateTimeOffset? LastFiredAt,
    JsonElement Template,
    IReadOnlyList<JobSummaryResponse> RecentRuns);

public record RedriveRequest(int? Attempts);

public record HandlerResponse(string Type, bool InProcessOnly);
