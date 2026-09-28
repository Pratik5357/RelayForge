using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TaskScheduler.Infrastructure.Insights;
using TaskScheduler.Infrastructure.Jobs;

namespace TaskScheduler.IntegrationTests;

public class JobApiTests : IClassFixture<SchedulerApiFactory>
{
    private readonly HttpClient _client;

    public JobApiTests(SchedulerApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SubmitAndWait_LinearDagSucceeds()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new
        {
            name = "linear",
            tasks = new object[]
            {
                new { key = "extract", type = "delay", payload = new { milliseconds = 20 }, dependsOn = Array.Empty<string>() },
                new { key = "load", type = "echo", payload = new { ok = true }, dependsOn = new[] { "extract" } }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JobStatusDto>(JsonOptions());
        Assert.NotNull(created);

        var job = await WaitForTerminalAsync(created!.Id);
        Assert.Equal("Succeeded", job.State);
        Assert.All(job.Tasks, t => Assert.Equal("Succeeded", t.State));
        Assert.Equal("extract", job.Tasks.Single(t => t.Key == "load").DependsOn.Single());
    }

    [Fact]
    public async Task FailHandler_RetriesThenDeadLetters()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new
        {
            name = "flaky",
            tasks = new object[]
            {
                new { key = "boom", type = "fail", payload = new { }, maxAttempts = 2, dependsOn = Array.Empty<string>() }
            }
        });

        var created = await response.Content.ReadFromJsonAsync<JobStatusDto>(JsonOptions());
        var job = await WaitForTerminalAsync(created!.Id, TimeSpan.FromSeconds(15));
        Assert.Equal("Failed", job.State);
        var task = Assert.Single(job.Tasks);
        Assert.Equal("DeadLettered", task.State);
        Assert.Equal(2, task.AttemptCount);
    }

    [Fact]
    public async Task Cycle_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new
        {
            name = "cycle",
            tasks = new object[]
            {
                new { key = "a", type = "echo", dependsOn = new[] { "b" } },
                new { key = "b", type = "echo", dependsOn = new[] { "a" } }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListAndCancel_Work()
    {
        var create = await _client.PostAsJsonAsync("/api/jobs", new
        {
            name = "to-cancel",
            tasks = new object[]
            {
                new { key = "slow", type = "echo", payload = new { n = 1 }, dependsOn = Array.Empty<string>() }
            }
        });
        var created = await create.Content.ReadFromJsonAsync<JobStatusDto>(JsonOptions());

        var listedResponse = await _client.GetAsync("/api/jobs");
        var listedBody = await listedResponse.Content.ReadAsStringAsync();
        Assert.True(listedResponse.IsSuccessStatusCode, listedBody);
        var listed = JsonSerializer.Deserialize<List<JobStatusDto>>(listedBody, JsonOptions());
        Assert.Contains(listed!, j => j.Id == created!.Id);

        var cancel = await _client.PostAsync($"/api/jobs/{created!.Id}/cancel", null);
        Assert.True(cancel.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Cancel_DuringRun_SettlesAsCancelled()
    {
        // The first task is still running when the cancel lands, so the job's final
        // state has to account for the cancel rather than the tracked copy of the flag.
        var create = await _client.PostAsJsonAsync("/api/jobs", new
        {
            name = "cancel-mid-run",
            tasks = new object[]
            {
                new { key = "slow", type = "delay", payload = new { milliseconds = 1500 }, dependsOn = Array.Empty<string>() },
                new { key = "next", type = "echo", payload = new { }, dependsOn = new[] { "slow" } }
            }
        });
        var created = await create.Content.ReadFromJsonAsync<JobStatusDto>(JsonOptions());

        await Task.Delay(300);
        var cancel = await _client.PostAsync($"/api/jobs/{created!.Id}/cancel", null);
        Assert.True(cancel.IsSuccessStatusCode);

        var job = await WaitForTerminalAsync(created.Id, TimeSpan.FromSeconds(15));
        Assert.Equal("Cancelled", job.State);
        Assert.Equal("Succeeded", job.Tasks.Single(t => t.Key == "slow").State);
        Assert.Equal("Cancelled", job.Tasks.Single(t => t.Key == "next").State);
    }

    [Fact]
    public async Task SystemInfo_ReportsWiringAndCounts()
    {
        var info = await _client.GetFromJsonAsync<SystemInfoDto>("/api/system", JsonOptions());

        Assert.NotNull(info);
        Assert.Equal("Sqlite", info!.Wiring.Database);
        Assert.Equal("Channel", info.Wiring.Queue);
        Assert.Equal("InMemory", info.Wiring.LockProvider);
        Assert.True(info.Wiring.InProcessWorkers);
        Assert.Equal(15, info.Wiring.LeaseSeconds);
        Assert.True(info.Counts.Jobs >= 0);
    }

    private async Task<JobStatusDto> WaitForTerminalAsync(Guid id, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        JobStatusDto? job = null;
        while (DateTime.UtcNow < deadline)
        {
            job = await _client.GetFromJsonAsync<JobStatusDto>($"/api/jobs/{id}", JsonOptions());
            if (job is { State: "Succeeded" or "Failed" or "PartiallyFailed" or "Cancelled" })
            {
                return job;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Job {id} stayed in {job?.State}");
    }

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
}

public sealed class SchedulerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"relayforge-it-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Scheduler", $"Data Source={_dbPath}");
        builder.UseSetting("Scheduler:Database", "Sqlite");
        builder.UseSetting("Scheduler:Queue", "Channel");
        builder.UseSetting("Scheduler:LockProvider", "InMemory");
        builder.UseSetting("Scheduler:RunInProcessWorkers", "true");
        builder.UseSetting("Scheduler:WorkerCount", "2");
        builder.UseSetting("Scheduler:LeaseDuration", "00:00:15");
        Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            File.Delete(_dbPath);
        }
        catch
        {
            // best-effort cleanup of the throwaway sqlite file
        }
    }
}
