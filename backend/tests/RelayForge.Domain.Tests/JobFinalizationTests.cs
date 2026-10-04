using RelayForge.Domain.Dag;
using RelayForge.Domain.Enums;

namespace RelayForge.Domain.Tests;

public class JobFinalizationTests
{
    private static JobFinalization.TaskInfo Task(Guid id, TaskState state, params Guid[] dependsOn) =>
        new(id, state, dependsOn);

    [Fact]
    public void Evaluate_AllSucceeded_IsFinalAndSucceeded()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = JobFinalization.Evaluate(new[]
        {
            Task(a, TaskState.Succeeded),
            Task(b, TaskState.Succeeded, a),
        });

        Assert.True(result.IsFinal);
        Assert.Equal(JobState.Succeeded, result.FinalState);
        Assert.Empty(result.NewlyCancelledTaskIds);
    }

    [Fact]
    public void Evaluate_StillHasRunnableWork_IsNotFinal()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var result = JobFinalization.Evaluate(new[]
        {
            Task(a, TaskState.Succeeded),
            Task(b, TaskState.Pending, a),
        });

        Assert.False(result.IsFinal);
        Assert.Null(result.FinalState);
    }

    [Fact]
    public void Evaluate_StillRunning_IsNotFinal()
    {
        var a = Guid.NewGuid();

        var result = JobFinalization.Evaluate(new[]
        {
            Task(a, TaskState.Running),
        });

        Assert.False(result.IsFinal);
    }

    [Fact]
    public void Evaluate_OneBranchDeadLettered_OneBranchSucceeded_IsPartiallyFailed()
    {
        // Diamond-free "two independent branches" shape: root -> {healthy, hopeless}
        var root = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        var hopeless = Guid.NewGuid();

        var result = JobFinalization.Evaluate(new[]
        {
            Task(root, TaskState.Succeeded),
            Task(healthy, TaskState.Succeeded, root),
            Task(hopeless, TaskState.DeadLettered, root),
        });

        Assert.True(result.IsFinal);
        Assert.Equal(JobState.PartiallyFailed, result.FinalState);
        Assert.Empty(result.NewlyCancelledTaskIds);
    }

    [Fact]
    public void Evaluate_AllDeadLettered_IsFailed()
    {
        var a = Guid.NewGuid();

        var result = JobFinalization.Evaluate(new[]
        {
            Task(a, TaskState.DeadLettered),
        });

        Assert.True(result.IsFinal);
        Assert.Equal(JobState.Failed, result.FinalState);
    }

    [Fact]
    public void Evaluate_PendingTaskDownstreamOfDeadLetter_IsMarkedCancelled_AndJobIsPartiallyFailed()
    {
        var healthy = Guid.NewGuid();
        var hopeless = Guid.NewGuid();
        var blocked = Guid.NewGuid(); // depends on the dead-lettered task, can never run

        var result = JobFinalization.Evaluate(new[]
        {
            Task(healthy, TaskState.Succeeded),
            Task(hopeless, TaskState.DeadLettered),
            Task(blocked, TaskState.Pending, hopeless),
        });

        Assert.True(result.IsFinal);
        Assert.Equal(JobState.PartiallyFailed, result.FinalState);
        Assert.Equal(new[] { blocked }, result.NewlyCancelledTaskIds);
    }

    [Fact]
    public void Evaluate_TransitiveChainBehindDeadLetter_AllGetCancelled()
    {
        var hopeless = Guid.NewGuid();
        var blockedDirect = Guid.NewGuid();
        var blockedTransitive = Guid.NewGuid(); // depends on blockedDirect, not on hopeless directly

        var result = JobFinalization.Evaluate(new[]
        {
            Task(hopeless, TaskState.DeadLettered),
            Task(blockedDirect, TaskState.Pending, hopeless),
            Task(blockedTransitive, TaskState.Pending, blockedDirect),
        });

        Assert.True(result.IsFinal);
        Assert.Equal(JobState.Failed, result.FinalState); // nothing ever succeeded
        Assert.Equal(
            new HashSet<Guid> { blockedDirect, blockedTransitive },
            result.NewlyCancelledTaskIds.ToHashSet());
    }

    [Fact]
    public void Evaluate_PendingTaskWaitingOnBackoff_StillCountsAsRunnable_NotFinal()
    {
        // A Pending task with no unmet dependency is "runnable" regardless of whether it's
        // mid-backoff -- NextAttemptAt isn't part of this pure evaluation, callers guard
        // re-enqueue separately. Finalization must not fire while it's still Pending.
        var a = Guid.NewGuid();

        var result = JobFinalization.Evaluate(new[]
        {
            Task(a, TaskState.Pending),
        });

        Assert.False(result.IsFinal);
    }
}
