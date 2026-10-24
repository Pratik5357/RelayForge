using System.Diagnostics;
using System.Net.Http.Json;
using RelayForge.Api.Contracts;
using static RelayForge.IntegrationTests.RelayForgeHost;

namespace RelayForge.IntegrationTests;

/// <summary>
/// Runs only where the runner image exists (build it with
/// `docker build --target runner -t relayforge-runner backend`); skipped elsewhere, e.g. in CI.
/// </summary>
public sealed class RunnerImageFactAttribute : FactAttribute
{
    public RunnerImageFactAttribute()
    {
        if (!ImageExists("relayforge-runner"))
        {
            Skip = "The relayforge-runner image isn't built.";
        }
    }

    private static bool ImageExists(string image)
    {
        try
        {
            using var inspect = Process.Start(new ProcessStartInfo("docker", $"image inspect {image}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            inspect.WaitForExit(10_000);
            return inspect.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

[Collection(PostgresCollection.Name)]
public class ContainerIsolationTests
{
    private readonly PostgresFixture _postgres;

    public ContainerIsolationTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [RunnerImageFact]
    public async Task Container_isolation_runs_the_task_in_a_throwaway_container()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 1);
        var job = await host.SubmitAsync(Simulated("boxed", new { durationMs = 500, output = new { from = "container" } }, isolation: "Container"));

        var finished = await host.WaitForFinishAsync(job.Id, TimeSpan.FromSeconds(60));

        Assert.Equal("Succeeded", finished.State);
        var task = finished.Tasks.Single();
        Assert.Equal("container", task.Output!.Value.GetProperty("from").GetString());
        var logs = await host.CreateClient().GetFromJsonAsync<List<TaskLogResponse>>($"/api/tasks/{task.Id}/logs");
        Assert.Contains(logs!, l => l.Line.StartsWith("Started container relayforge-"));
    }

    [RunnerImageFact]
    public async Task Runaway_task_in_a_container_is_killed_at_its_time_limit()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 1);
        var job = await host.SubmitAsync(Simulated("runaway", new { durationMs = 60_000, ignoreCancellation = true },
            maxAttempts: 1, isolation: "Container", timeoutMs: 3000));

        var finished = await host.WaitForFinishAsync(job.Id, TimeSpan.FromSeconds(30));
        var task = finished.Tasks.Single();

        Assert.Equal("DeadLettered", task.State);
        Assert.StartsWith("Timed out", task.ErrorMessage);

        // The container was killed, not left running: docker run --rm removes it on exit.
        using var ps = Process.Start(new ProcessStartInfo("docker", $"ps -q --filter name=relayforge-{task.Id:N}") { RedirectStandardOutput = true })!;
        var running = (await ps.StandardOutput.ReadToEndAsync()).Trim();
        await ps.WaitForExitAsync();
        Assert.Equal("", running);
    }
}
