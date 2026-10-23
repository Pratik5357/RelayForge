using RelayForge.Domain.Enums;

namespace RelayForge.Domain.Dag;

/// <summary>
/// Pure evaluation of "is this job done, and what happened" given the current state of its
/// tasks (Ready tasks -- claimable, or waiting out a retry backoff -- count as still in
/// flight). Two responsibilities:
///   1. Identify Pending tasks that can never become ready because they transitively depend
///      on a failed (DeadLettered or Failed) or already-blocked task -- these should be marked
///      Cancelled.
///   2. Decide whether the job as a whole is finished, and if so, its final state:
///        all Succeeded                                  -> Succeeded
///        at least one Succeeded AND at least one failed/blocked -> PartiallyFailed
///        zero Succeeded AND at least one failed           -> Failed
/// No DB access -- callers own applying NewlyCancelledTaskIds and FinalState to the actual
/// entities.
/// </summary>
public static class JobFinalization
{
    public record TaskInfo(Guid Id, TaskState State, IReadOnlyList<Guid> DependsOn);

    public record Result(IReadOnlyList<Guid> NewlyCancelledTaskIds, bool IsFinal, JobState? FinalState);

    public static Result Evaluate(IReadOnlyList<TaskInfo> tasks)
    {
        var stateById = tasks.ToDictionary(t => t.Id, t => t.State);
        var blocked = new HashSet<Guid>();

        var changed = true;
        while (changed)
        {
            changed = false;

            foreach (var t in tasks)
            {
                if (t.State != TaskState.Pending || blocked.Contains(t.Id))
                {
                    continue;
                }

                var blockedByDependency = t.DependsOn.Any(depId =>
                    stateById.TryGetValue(depId, out var depState) &&
                    (IsFailure(depState) || blocked.Contains(depId)));

                if (blockedByDependency)
                {
                    blocked.Add(t.Id);
                    changed = true;
                }
            }
        }

        var hasRunning = tasks.Any(t => t.State is TaskState.Running or TaskState.Ready);
        var hasRunnablePending = tasks.Any(t => t.State == TaskState.Pending && !blocked.Contains(t.Id));

        if (hasRunning || hasRunnablePending)
        {
            return new Result(blocked.ToList(), IsFinal: false, FinalState: null);
        }

        var anySucceeded = tasks.Any(t => t.State == TaskState.Succeeded);
        var anyDeadOrBlocked = tasks.Any(t => IsFailure(t.State)) || blocked.Count > 0;

        JobState finalState = (anySucceeded, anyDeadOrBlocked) switch
        {
            (true, false) => JobState.Succeeded,
            (true, true) => JobState.PartiallyFailed,
            (false, true) => JobState.Failed,
            (false, false) => JobState.Succeeded,
        };

        return new Result(blocked.ToList(), IsFinal: true, FinalState: finalState);
    }

    private static bool IsFailure(TaskState state) => state is TaskState.DeadLettered or TaskState.Failed;
}
