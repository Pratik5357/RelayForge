using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RelayForge.Api.Contracts;
using RelayForge.Handlers;
using RelayForge.Infrastructure;
using RelayForge.Infrastructure.Execution;
using RelayForge.Infrastructure.Jobs;
using static RelayForge.IntegrationTests.RelayForgeHost;

namespace RelayForge.IntegrationTests;

/// <summary>
/// Phase 3: real handlers (A) -- retryable vs fatal, outputs flowing to dependents, the HTTP
/// handler and idempotency keys -- and the transactional outbox (D).
/// </summary>
[Collection(PostgresCollection.Name)]
public class HandlerAndOutboxTests
{
    private readonly PostgresFixture _postgres;

    public HandlerAndOutboxTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Fatal_failure_skips_retries_and_blocks_dependents()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 2);
        var job = await host.SubmitAsync(
            Step("ok"),
            Simulated("bad-request", new { fatalOnAttempt = 1 }, maxAttempts: 4),
            Step("needs-it", "bad-request"));

        var finished = await host.WaitForFinishAsync(job.Id);

        Assert.Equal("PartiallyFailed", finished.State);
        var bad = finished.Tasks.Single(t => t.Key == "bad-request");
        Assert.Equal("Failed", bad.State);
        Assert.Equal(1, bad.AttemptCount);
        Assert.Equal("Fatal", Assert.Single(bad.Attempts).Outcome);
        Assert.Equal("Cancelled", finished.Tasks.Single(t => t.Key == "needs-it").State);
    }

    [Fact]
    public async Task Outputs_flow_to_dependents_by_key()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 2);
        var job = await host.SubmitAsync(
            Simulated("fetch", new { output = new { rows = 42 } }),
            Simulated("count", new { output = new { ok = true } }),
            new SubmitJobTaskRequest("report", "report", DependsOn: ["fetch", "count"], Type: "test.echo"));

        var finished = await host.WaitForFinishAsync(job.Id);

        Assert.Equal("Succeeded", finished.State);
        var output = finished.Tasks.Single(t => t.Key == "report").Output!.Value;
        Assert.Equal(42, output.GetProperty("fetch").GetProperty("rows").GetInt32());
        Assert.True(output.GetProperty("count").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Http_handler_retries_5xx_with_a_stable_idempotency_key_and_fails_fast_on_4xx()
    {
        var keys = new ConcurrentQueue<string>();
        var calls = 0;

        await using var remote = await StartRemoteAsync(async ctx =>
        {
            keys.Enqueue(ctx.Request.Headers["Idempotency-Key"].ToString());

            if (ctx.Request.Path == "/flaky")
            {
                ctx.Response.StatusCode = Interlocked.Increment(ref calls) < 3 ? 503 : 200;
                await ctx.Response.WriteAsJsonAsync(new { calls });
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }
        });

        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 2);
        var job = await host.SubmitAsync(
            Http("flaky", $"{remote.Url}/flaky"),
            Http("missing", $"{remote.Url}/missing"));

        var finished = await host.WaitForFinishAsync(job.Id);

        var flaky = finished.Tasks.Single(t => t.Key == "flaky");
        Assert.Equal("Succeeded", flaky.State);
        Assert.Equal(3, flaky.AttemptCount);
        Assert.Equal(200, flaky.Output!.Value.GetProperty("status").GetInt32());
        Assert.Equal(3, flaky.Output!.Value.GetProperty("body").GetProperty("calls").GetInt32());

        var missing = finished.Tasks.Single(t => t.Key == "missing");
        Assert.Equal("Failed", missing.State);
        Assert.Equal(1, missing.AttemptCount);
        Assert.Contains("404", missing.ErrorMessage);

        // Same key on every retry of a task; different tasks, different keys.
        Assert.Equal(3, keys.Count(k => k == flaky.IdempotencyKey.ToString()));
        Assert.Equal(1, keys.Count(k => k == missing.IdempotencyKey.ToString()));
    }

    [Fact]
    public async Task Submit_rejects_unknown_types_and_bad_payloads()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var client = host.CreateClient();

        var unknown = await client.PostAsJsonAsync("/api/jobs", new SubmitJobRequest(null, null,
            [new SubmitJobTaskRequest("a", "a", Type: "no.such.handler")]));
        var badUrl = await client.PostAsJsonAsync("/api/jobs", new SubmitJobRequest(null, null,
            [new SubmitJobTaskRequest("a", "a", Type: "http.request", Payload: JsonSerializer.SerializeToElement(new { url = "not a url" }))]));
        var inProcessOnly = await client.PostAsJsonAsync("/api/jobs", new SubmitJobRequest(null, null,
            [new SubmitJobTaskRequest("a", "a", Type: "retention", Isolation: "Process")]));

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("no.such.handler", await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badUrl.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inProcessOnly.StatusCode);
    }

    [Fact]
    public async Task Idempotent_submit_returns_the_first_job_and_refuses_a_changed_body()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var client = host.CreateClient();
        var request = new SubmitJobRequest("nightly", null, [Step("a")]);

        async Task<HttpResponseMessage> Send(SubmitJobRequest body)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/api/jobs") { Content = JsonContent.Create(body) };
            message.Headers.Add("Idempotency-Key", "order-1234");
            return await client.SendAsync(message);
        }

        var first = await Send(request);
        var again = await Send(request);
        var changed = await Send(request with { Name = "different" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<JobDetailResponse>())!.Id,
            (await again.Content.ReadFromJsonAsync<JobDetailResponse>())!.Id);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, changed.StatusCode);
        Assert.Single(await client.GetFromJsonAsync<List<JobSummaryResponse>>("/api/jobs") ?? []);
    }

    [Fact]
    public async Task Events_are_numbered_without_gaps_dispatched_and_replayable()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 2);
        var job = await host.SubmitAsync(
            Simulated("a", new { failUntilAttempt = 2 }),
            Step("b", "a"),
            Step("c", "a"));

        var finished = await host.WaitForFinishAsync(job.Id);
        var events = await host.CreateClient().GetFromJsonAsync<List<JobChangedEvent>>($"/api/jobs/{job.Id}/events");

        Assert.Equal(Enumerable.Range(1, events!.Count), events.Select(e => e.Sequence));
        Assert.Equal(finished.LastEventSequence, events[^1].Sequence);
        Assert.Equal("JobSubmitted", events[0].Type);
        Assert.Equal("JobFinished", events[^1].Type);
        Assert.Equal("Succeeded", events[^1].JobState);
        Assert.Contains(events, e => e.Type == "TaskRetrying" && e.TaskName == "a");

        var tail = await host.CreateClient().GetFromJsonAsync<List<JobChangedEvent>>($"/api/jobs/{job.Id}/events?after={events.Count - 2}");
        Assert.Equal([events.Count - 1, events.Count], tail!.Select(e => e.Sequence));

        // Every event reaches the live notifier, via the dispatcher.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (host.Notifier.Events.Count(e => e.JobId == job.Id) < events.Count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(events.Select(e => e.Sequence), host.Notifier.Events.Where(e => e.JobId == job.Id).Select(e => e.Sequence).Order());
        Assert.Equal(0, await host.WithAsync<RelayForgeDbContext, int>(db => db.OutboxEvents.CountAsync(e => e.DispatchedAt == null)));
    }

    [Fact]
    public async Task Rolled_back_change_leaves_no_event()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var job = await host.SubmitAsync(Step("a"));
        var before = (await host.GetJobAsync(job.Id)).LastEventSequence;

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.WithAsync<JobOrchestrator, bool>(o =>
            o.ExecuteAsync(job.Id, (events, _) =>
            {
                events.Add(new JobEventDraft("Test", "never committed"));
                throw new InvalidOperationException("boom");
            }, default)));

        Assert.Equal(before, (await host.GetJobAsync(job.Id)).LastEventSequence);
        var events = await host.CreateClient().GetFromJsonAsync<List<JobChangedEvent>>($"/api/jobs/{job.Id}/events");
        Assert.DoesNotContain(events!, e => e.Type == "Test");
    }

    private static SubmitJobTaskRequest Http(string key, string url) =>
        new(key, key, MaxAttempts: 4, Type: "http.request", Payload: JsonSerializer.SerializeToElement(new { url }, HandlerJson.Options));

    private sealed class RemoteServer(WebApplication app, string url) : IAsyncDisposable
    {
        public string Url { get; } = url;

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    /// <summary>A real HTTP server on a free port, standing in for an external API.</summary>
    private static async Task<RemoteServer> StartRemoteAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Run(handler);
        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return new RemoteServer(app, url);
    }
}
