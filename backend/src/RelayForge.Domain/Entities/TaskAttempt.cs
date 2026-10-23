using RelayForge.Domain.Enums;

namespace RelayForge.Domain.Entities;

/// <summary>One attempt at a task: who ran it, when, and how it ended. Written at claim time.</summary>
public class TaskAttempt
{
    public long Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid JobId { get; set; }
    public int Attempt { get; set; }
    public Guid WorkerId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Null while the attempt is still running.</summary>
    public AttemptOutcome? Outcome { get; set; }
    public string? Error { get; set; }

    /// <summary>
    /// Set when this attempt's worker reported a result after losing its lease (a zombie):
    /// what it tried to report. The fence rejected it, so it changed nothing.
    /// </summary>
    public string? RejectedLateResult { get; set; }
}
