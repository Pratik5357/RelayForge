using Microsoft.EntityFrameworkCore;
using Npgsql;
using RelayForge.Domain.Enums;
using RelayForge.Infrastructure;
using RelayForge.Infrastructure.Execution;
using RelayForge.Infrastructure.Jobs;
using static RelayForge.IntegrationTests.RelayForgeHost;

namespace RelayForge.IntegrationTests;

/// <summary>
/// Phase 1 guarantees, proved against a real Postgres: readiness survives a restart (F1), a
/// stale worker can't overwrite a newer attempt (F2), and concurrent completions of one job
/// can't race each other (F5).
/// </summary>
[Collection(PostgresCollection.Name)]
public class DurableCoreTests
{
    private static readonly AttemptReport Success = new(AttemptOutcome.Succeeded);

    private readonly PostgresFixture _postgres;

    public DurableCoreTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Job_submitted_before_a_restart_finishes_on_the_next_host()
    {
        var database = _postgres.NewDatabase();
        Guid jobId;

        // A host with no workers accepts the job, then "crashes" before anything claims it.
        await using (var first = new RelayForgeHost(database, workers: 0))
        {
            var job = await first.SubmitAsync(Step("extract"), Step("load", "extract"));
            jobId = job.Id;

            Assert.Equal("Ready", job.Tasks.Single(t => t.Name == "extract").State);
        }

        await using var second = new RelayForgeHost(database, workers: 2);
        var finished = await second.WaitForFinishAsync(jobId);

        Assert.Equal("Succeeded", finished.State);
        Assert.All(finished.Tasks, t => Assert.Equal("Succeeded", t.State));
    }

    [Fact]
    public async Task Stale_worker_cannot_complete_a_reclaimed_task()
    {
        // No workers and no sweep in this host; the test claims and reclaims by hand.
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var job = await host.SubmitAsync(Step("charge"));
        var workerA = Guid.NewGuid();
        var workerB = Guid.NewGuid();

        var claimA = await host.WithAttemptsAsync(a => a.TryClaimAsync(workerA, default));
        Assert.NotNull(claimA);
        Assert.Equal(1, claimA.AttemptCount);

        // Worker A stalls past its lease; the sweep reclaims; worker B takes attempt 2.
        await host.ExecuteSqlAsync("""UPDATE "JobTasks" SET "LeaseExpiresAt" = now() - interval '1 second'""");
        Assert.Equal(1, await host.WithAttemptsAsync(a => a.ReclaimExpiredAsync(default)));
        var claimB = await ClaimWhenDueAsync(host, workerB);
        Assert.Equal(2, claimB.AttemptCount);

        // A wakes up and reports success: rejected, and B still owns the row.
        Assert.False(await host.WithAttemptsAsync(a => a.CompleteAsync(claimA, workerA, Success, default)));
        var task = (await host.GetJobAsync(job.Id)).Tasks.Single();
        Assert.Equal("Running", task.State);
        Assert.Equal(2, task.AttemptCount);

        Assert.True(await host.WithAttemptsAsync(a => a.CompleteAsync(claimB, workerB, Success, default)));
        Assert.Equal("Succeeded", (await host.GetJobAsync(job.Id)).State);
    }

    [Fact]
    public async Task Reclaim_does_not_overwrite_a_completion_that_landed_first()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var job = await host.SubmitAsync(Step("report"));
        var worker = Guid.NewGuid();

        var claim = await host.WithAttemptsAsync(a => a.TryClaimAsync(worker, default));
        await host.ExecuteSqlAsync("""UPDATE "JobTasks" SET "LeaseExpiresAt" = now() - interval '1 second'""");

        // The worker finishes just before the sweep gets to it. Nobody else claimed the task,
        // so the late-but-uncontested result is accepted, and the reclaim finds nothing to do.
        Assert.True(await host.WithAttemptsAsync(a => a.CompleteAsync(claim!, worker, Success, default)));
        Assert.Equal(0, await host.WithAttemptsAsync(a => a.ReclaimExpiredAsync(default)));

        var finished = await host.GetJobAsync(job.Id);
        Assert.Equal("Succeeded", finished.State);
        Assert.Equal(1, finished.Tasks.Single().AttemptCount);
    }

    [Fact]
    public async Task Concurrent_completions_promote_and_finalize_exactly_once()
    {
        // root -> 8 parallel branches -> sink. The 8 branches all complete at the same moment.
        const int branches = 8;
        var branchKeys = Enumerable.Range(1, branches).Select(i => $"branch{i}").ToArray();

        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var job = await host.SubmitAsync(
            [Step("root"), .. branchKeys.Select(k => Step(k, "root")), Step("sink", branchKeys)]);

        await ClaimAndCompleteAsync(host, count: 1);

        var claims = await ClaimAllAsync(host, branches);
        var results = await Task.WhenAll(claims.Select(c =>
            host.WithAttemptsAsync(a => a.CompleteAsync(c.Claim, c.WorkerId, Success, default))));
        Assert.All(results, Assert.True);

        // The sink was promoted exactly once and is the only thing left to claim.
        var afterBranches = await host.GetJobAsync(job.Id);
        Assert.Equal("Ready", afterBranches.Tasks.Single(t => t.Name == "sink").State);

        await ClaimAndCompleteAsync(host, count: 1);
        Assert.Null(await host.WithAttemptsAsync(a => a.TryClaimAsync(Guid.NewGuid(), default)));

        var finished = await host.GetJobAsync(job.Id);
        Assert.Equal("Succeeded", finished.State);
        var finishedEvents = await host.WithAsync<RelayForgeDbContext, int>(db =>
            db.OutboxEvents.CountAsync(e => e.JobId == job.Id && e.Type == "JobFinished"));
        Assert.Equal(1, finishedEvents);
    }

    [Fact]
    public async Task Completion_waits_for_the_job_row_lock()
    {
        // Deterministic version of the race above: whoever holds the job's row lock (here, an
        // outside transaction) holds off every other mutation of that job until it commits.
        var database = _postgres.NewDatabase();
        await using var host = new RelayForgeHost(database, workers: 0);
        // Two steps, so completing "a" only promotes "b": nothing in it writes the job row
        // itself, and only the lock taken up front can make it wait.
        var job = await host.SubmitAsync(Step("a"), Step("b", "a"));
        var worker = Guid.NewGuid();
        var claim = await host.WithAttemptsAsync(a => a.TryClaimAsync(worker, default));

        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using (var lockJob = new NpgsqlCommand($"""SELECT 1 FROM "Jobs" WHERE "Id" = '{job.Id}' FOR UPDATE""", connection, tx))
        {
            await lockJob.ExecuteNonQueryAsync();
        }

        var completion = host.WithAttemptsAsync(a => a.CompleteAsync(claim!, worker, Success, default));
        await Task.Delay(500);
        Assert.False(completion.IsCompleted);

        await tx.CommitAsync();
        Assert.True(await completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Ready", (await host.GetJobAsync(job.Id)).Tasks.Single(t => t.Name == "b").State);
    }

    [Fact]
    public async Task Failed_attempt_retries_and_then_succeeds_end_to_end()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 4);
        var job = await host.SubmitAsync(
            new SubmitJobTaskRequest("flaky", "flaky", 20, [], MaxAttempts: 3, FailUntilAttempt: 2),
            Step("after", "flaky"));

        var finished = await host.WaitForFinishAsync(job.Id);

        Assert.Equal("Succeeded", finished.State);
        Assert.Equal(2, finished.Tasks.Single(t => t.Name == "flaky").AttemptCount);
        Assert.Equal(1, finished.Tasks.Single(t => t.Name == "after").AttemptCount);
    }

    [Fact]
    public async Task Cancel_calls_off_ready_and_pending_tasks_in_one_step()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var job = await host.SubmitAsync(Step("a"), Step("b", "a"));

        var response = await host.CreateClient().PostAsync($"/api/jobs/{job.Id}/cancel", null);
        response.EnsureSuccessStatusCode();

        var cancelled = await host.GetJobAsync(job.Id);
        Assert.Equal("Cancelled", cancelled.State);
        Assert.All(cancelled.Tasks, t => Assert.Equal("Cancelled", t.State));
        Assert.Null(await host.WithAttemptsAsync(a => a.TryClaimAsync(Guid.NewGuid(), default)));
    }

    private static async Task<ClaimedTask> ClaimWhenDueAsync(RelayForgeHost host, Guid workerId)
    {
        // A reclaimed task waits out its retry backoff (50-200 ms here) before it's claimable.
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            var claim = await host.WithAttemptsAsync(a => a.TryClaimAsync(workerId, default));

            if (claim is not null)
            {
                return claim;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("No task became claimable.");
    }

    private static async Task<List<(ClaimedTask Claim, Guid WorkerId)>> ClaimAllAsync(RelayForgeHost host, int count)
    {
        var claims = new List<(ClaimedTask, Guid)>();

        for (var i = 0; i < count; i++)
        {
            var workerId = Guid.NewGuid();
            claims.Add((await ClaimWhenDueAsync(host, workerId), workerId));
        }

        return claims;
    }

    private static async Task ClaimAndCompleteAsync(RelayForgeHost host, int count)
    {
        foreach (var (claim, workerId) in await ClaimAllAsync(host, count))
        {
            Assert.True(await host.WithAttemptsAsync(a => a.CompleteAsync(claim, workerId, Success, default)));
        }
    }
}
