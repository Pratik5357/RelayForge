using static RelayForge.IntegrationTests.RelayForgeHost;

namespace RelayForge.IntegrationTests;

/// <summary>
/// Phase 2 guarantees: heartbeated leases (F3), cancelling running work (F4), graceful
/// shutdown (F5), attempt history and the two chaos scenarios (C). Leases are short here
/// (600 ms, renewed every 200 ms) so failures surface in about a second.
/// </summary>
[Collection(PostgresCollection.Name)]
public class LeaseTests
{
    private static readonly Dictionary<string, string?> ShortLease = new()
    {
        ["Reliability:LeaseDurationMs"] = "600",
        ["Reliability:SweepIntervalMs"] = "100",
    };

    private readonly PostgresFixture _postgres;

    public LeaseTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Heartbeat_keeps_a_task_longer_than_its_lease_alive()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 1, ShortLease);

        // 2.5 s of work on a 0.6 s lease: only the heartbeat keeps it from being reclaimed.
        var job = await host.SubmitAsync(Simulated("long", new { durationMs = 2500 }));
        var finished = await host.WaitForFinishAsync(job.Id);

        Assert.Equal("Succeeded", finished.State);
        var task = finished.Tasks.Single();
        Assert.Equal(1, task.AttemptCount);
        Assert.Equal("Succeeded", Assert.Single(task.Attempts).Outcome);
        Assert.Equal(0, finished.Invariants.LeasesReclaimed);
    }

    [Fact]
    public async Task Worker_crash_is_reclaimed_and_retried_once_without_duplicates()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 2, ShortLease);

        var job = await host.SubmitAsync(Simulated("charge", new { durationMs = 200, crashOnAttempt = 1 }));
        var finished = await host.WaitForFinishAsync(job.Id);

        Assert.Equal("Succeeded", finished.State);
        var attempts = finished.Tasks.Single().Attempts;
        Assert.Equal(2, attempts.Count);
        Assert.Equal("LeaseLost", attempts[0].Outcome);
        Assert.Equal("Succeeded", attempts[1].Outcome);
        Assert.Equal(new(2, 1, 0, 1, 0), finished.Invariants);
    }

    [Fact]
    public async Task Zombie_result_after_the_lease_is_lost_is_rejected()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 2, ShortLease);

        // Attempt 1 freezes for 2 s (lease: 0.6 s), is reclaimed, attempt 2 succeeds, then
        // attempt 1 wakes up and reports success too.
        var job = await host.SubmitAsync(Simulated("report", new { durationMs = 100, pauseOnAttempt = 1, pauseMs = 2000 }));

        var settled = await host.WaitForAsync(job.Id, j => j.Invariants.StaleResultsRejected == 1, TimeSpan.FromSeconds(15));

        Assert.Equal("Succeeded", settled.State);
        var attempts = settled.Tasks.Single().Attempts;
        Assert.Equal("LeaseLost", attempts[0].Outcome);
        Assert.StartsWith("Succeeded", attempts[0].RejectedLateResult);
        Assert.Equal("Succeeded", attempts[1].Outcome);
        Assert.Equal(0, settled.Invariants.DuplicateCompletions);
        Assert.Equal(1, settled.Invariants.SucceededAttempts);
        Assert.Contains(host.Notifier.Events, e => e.JobId == job.Id && e.Type == "StaleResultRejected");
    }

    [Fact]
    public async Task Cancel_stops_a_running_task_promptly()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 1, ShortLease);
        var job = await host.SubmitAsync(Simulated("slow", new { durationMs = 30_000 }), Step("after", "slow"));
        await host.WaitForAsync(job.Id, j => j.Tasks.Single(t => t.Key == "slow").State == "Running");

        var started = DateTime.UtcNow;
        (await host.PostAsync($"/api/jobs/{job.Id}/cancel")).EnsureSuccessStatusCode();
        var finished = await host.WaitForFinishAsync(job.Id, TimeSpan.FromSeconds(5));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3), "cancel should not wait for the 30 s task");
        Assert.Equal("Cancelled", finished.State);
        var slow = finished.Tasks.Single(t => t.Key == "slow");
        Assert.Equal("Cancelled", slow.State);
        Assert.Equal("Cancelled", Assert.Single(slow.Attempts).Outcome);
        Assert.Equal("Cancelled", finished.Tasks.Single(t => t.Key == "after").State);
    }

    [Fact]
    public async Task Graceful_shutdown_hands_running_work_back_without_spending_a_retry()
    {
        var database = _postgres.NewDatabase();
        Guid jobId;

        await using (var first = new RelayForgeHost(database, workers: 1, ShortLease))
        {
            var job = await first.SubmitAsync(Simulated("deploy-me", new { durationMs = 3000 }, maxAttempts: 1));
            jobId = job.Id;
            await first.WaitForAsync(jobId, j => j.Tasks.Single().State == "Running");
        }

        // Stopped with the task mid-attempt: handed back to Ready, budget grown by one.
        await using var second = new RelayForgeHost(database, workers: 1, ShortLease);
        var finished = await second.WaitForFinishAsync(jobId, TimeSpan.FromSeconds(15));

        Assert.Equal("Succeeded", finished.State);
        var task = finished.Tasks.Single();
        Assert.Equal(2, task.MaxAttempts);
        Assert.Equal(["Released", "Succeeded"], task.Attempts.Select(a => a.Outcome));
    }

    [Fact]
    public async Task Timed_out_attempt_counts_as_a_retryable_failure()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 1, ShortLease);
        var job = await host.SubmitAsync(Simulated("stuck", new { durationMs = 10_000 }, maxAttempts: 2, timeoutMs: 300));

        var finished = await host.WaitForFinishAsync(job.Id, TimeSpan.FromSeconds(10));

        Assert.Equal("Failed", finished.State);
        var task = finished.Tasks.Single();
        Assert.Equal("DeadLettered", task.State);
        Assert.All(task.Attempts, a => Assert.StartsWith("Timed out", a.Error));
        Assert.Equal(2, task.Attempts.Count);
    }
}
