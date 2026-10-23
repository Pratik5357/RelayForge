using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RelayForge.Infrastructure.Execution;
using RelayForge.Infrastructure.Jobs;
using static RelayForge.IntegrationTests.RelayForgeHost;

namespace RelayForge.IntegrationTests;

/// <summary>
/// Phase 4 (E): several independent hosts -- separate DI containers, connection pools and
/// NOTIFY listeners, coordinating only through Postgres -- standing in for separate processes.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ScaleOutTests
{
    private readonly PostgresFixture _postgres;

    public ScaleOutTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Many_workers_share_the_queue_without_duplicate_completions()
    {
        var database = _postgres.NewDatabase();
        var settings = new Dictionary<string, string?> { ["WorkerPool:Concurrency"] = "3" };

        await using var api = new RelayForgeHost(database, workers: 0);
        _ = api.Server; // migrate before the workers start polling
        await using var workerA = new RelayForgeHost(database, workers: 3, settings);
        await using var workerB = new RelayForgeHost(database, workers: 3, settings);
        _ = workerA.Server;
        _ = workerB.Server;

        var jobs = new List<Guid>();

        for (var i = 0; i < 20; i++)
        {
            var job = await api.SubmitAsync(new SubmitJobRequest($"load-{i}", null,
            [
                Simulated("extract", new { durationMs = 30 }),
                Simulated("left", new { durationMs = 30 }, ["extract"]),
                Simulated("right", new { durationMs = 30, failUntilAttempt = 2 }, ["extract"]),
                Simulated("load", new { durationMs = 30 }, ["left", "right"]),
            ]));
            jobs.Add(job.Id);
        }

        var finished = new List<RelayForge.Api.Contracts.JobDetailResponse>();

        foreach (var id in jobs)
        {
            finished.Add(await api.WaitForFinishAsync(id, TimeSpan.FromSeconds(60)));
        }

        Assert.All(finished, j => Assert.Equal("Succeeded", j.State));
        Assert.All(finished, j => Assert.Equal(0, j.Invariants.DuplicateCompletions));
        Assert.Equal(80, finished.Sum(j => j.Invariants.SucceededAttempts));

        // Work really was spread over both worker hosts' consumers (3 each).
        var workers = finished.SelectMany(j => j.Tasks).SelectMany(t => t.Attempts).Select(a => a.Worker).Distinct().Count();
        Assert.True(workers > 3, $"expected attempts on more than one host's 3 consumers, saw {workers}");
    }

    [Fact]
    public async Task Exactly_one_sweep_leads_and_another_takes_over_when_it_dies()
    {
        var database = _postgres.NewDatabase();
        await using var api = new RelayForgeHost(database, workers: 0);
        _ = api.Server;

        var a = new RelayForgeHost(database, workers: 1);
        await using var b = new RelayForgeHost(database, workers: 1);

        try
        {
            _ = a.Server;
            _ = b.Server;

            await WaitUntilAsync(() => Sweep(a).IsLeader || Sweep(b).IsLeader);
            Assert.True(Sweep(a).IsLeader ^ Sweep(b).IsLeader, "exactly one sweep should lead");

            var (leader, follower) = Sweep(a).IsLeader ? (a, b) : (b, a);
            await leader.DisposeAsync();

            // The lock dies with the leader's connection; the follower retries every 5 s.
            await WaitUntilAsync(() => Sweep(follower).IsLeader, TimeSpan.FromSeconds(15));
        }
        finally
        {
            await a.DisposeAsync();
        }
    }

    private static ReliabilitySweepHostedService Sweep(RelayForgeHost host) =>
        host.Services.GetServices<IHostedService>().OfType<ReliabilitySweepHostedService>().Single();

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(100);
        }
    }
}
