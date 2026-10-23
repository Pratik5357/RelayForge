using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RelayForge.Api.Contracts;
using RelayForge.Domain.Enums;
using RelayForge.Handlers;
using RelayForge.Infrastructure;
using RelayForge.Infrastructure.Jobs;
using RelayForge.Infrastructure.Scheduling;
using static RelayForge.IntegrationTests.RelayForgeHost;

namespace RelayForge.IntegrationTests;

/// <summary>
/// Phase 5: the process harness and live logs (F), schedules (G), and operator actions,
/// fairness and retention (H).
/// </summary>
[Collection(PostgresCollection.Name)]
public class PlatformTests
{
    private readonly PostgresFixture _postgres;

    public PlatformTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Process_isolation_runs_the_handler_in_a_runner_and_streams_its_logs()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 1);
        var job = await host.SubmitAsync(Simulated("isolated", new { durationMs = 1200, output = new { from = "runner" } }, isolation: "Process"));

        var finished = await host.WaitForFinishAsync(job.Id);

        Assert.Equal("Succeeded", finished.State);
        var task = finished.Tasks.Single();
        Assert.Equal("runner", task.Output!.Value.GetProperty("from").GetString());

        var logs = await host.CreateClient().GetFromJsonAsync<List<TaskLogResponse>>($"/api/tasks/{task.Id}/logs");
        Assert.Contains(logs!, l => l.Line.StartsWith("Started runner process"));
        Assert.Contains(logs!, l => l.Line.Contains("Still working (1 s)"));
        Assert.Equal(Enumerable.Range(1, logs!.Count), logs.Select(l => l.Sequence));
    }

    [Fact]
    public async Task Runaway_handler_is_killed_by_the_process_harness()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 1);

        // Ignores cancellation and would run 60 s; the 1 s timeout kills the runner process.
        var job = await host.SubmitAsync(Simulated("runaway", new { durationMs = 60_000, ignoreCancellation = true },
            maxAttempts: 1, isolation: "Process", timeoutMs: 1000));

        var finished = await host.WaitForFinishAsync(job.Id, TimeSpan.FromSeconds(10));

        var task = finished.Tasks.Single();
        Assert.Equal("DeadLettered", task.State);
        Assert.StartsWith("Timed out", task.ErrorMessage);
        var logs = await host.CreateClient().GetFromJsonAsync<List<TaskLogResponse>>($"/api/tasks/{task.Id}/logs");
        Assert.Contains(logs!, l => l.Line.StartsWith("Killed the runner"));
    }

    [Fact]
    public async Task Redrive_reopens_a_partially_failed_job_and_finishes_it()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 2);

        // "flaky" needs 3 attempts but only has 2, so it dead-letters and blocks "after".
        var job = await host.SubmitAsync(
            Step("ok"),
            Simulated("flaky", new { failUntilAttempt = 3 }, maxAttempts: 2),
            Step("after", "flaky"));
        var failed = await host.WaitForFinishAsync(job.Id);
        Assert.Equal("PartiallyFailed", failed.State);
        Assert.Equal("Cancelled", failed.Tasks.Single(t => t.Key == "after").State);

        var flaky = failed.Tasks.Single(t => t.Key == "flaky");
        (await host.PostAsync($"/api/tasks/{flaky.Id}/redrive", new { attempts = 2 })).EnsureSuccessStatusCode();
        var finished = await host.WaitForFinishAsync(job.Id);

        Assert.Equal("Succeeded", finished.State);
        Assert.Equal(3, finished.Tasks.Single(t => t.Key == "flaky").AttemptCount);
        Assert.Equal("Succeeded", finished.Tasks.Single(t => t.Key == "after").State);

        // Only failed tasks can be redriven.
        var again = await host.PostAsync($"/api/tasks/{flaky.Id}/redrive");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Paused_job_starts_nothing_new_until_resumed()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var job = await host.SubmitAsync(Step("a"), Step("b", "a"));

        (await host.PostAsync($"/api/jobs/{job.Id}/pause")).EnsureSuccessStatusCode();
        Assert.Null(await host.WithAttemptsAsync(a => a.TryClaimAsync(Guid.NewGuid(), default)));

        (await host.PostAsync($"/api/jobs/{job.Id}/resume")).EnsureSuccessStatusCode();
        Assert.NotNull(await host.WithAttemptsAsync(a => a.TryClaimAsync(Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task Higher_priority_is_claimed_first_and_tenant_caps_hold_back_busy_tenants()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0, new() { ["Fairness:MaxRunningPerTenant"] = "1" });
        var low = await host.SubmitAsync(new SubmitJobRequest("low", null, [Step("a")], Priority: 0, TenantId: "acme"));
        var low2 = await host.SubmitAsync(new SubmitJobRequest("low2", null, [Step("a")], Priority: 0, TenantId: "acme"));
        var high = await host.SubmitAsync(new SubmitJobRequest("high", null, [Step("a")], Priority: 10, TenantId: "globex"));

        var first = await host.WithAttemptsAsync(a => a.TryClaimAsync(Guid.NewGuid(), default));
        var second = await host.WithAttemptsAsync(a => a.TryClaimAsync(Guid.NewGuid(), default));
        var third = await host.WithAttemptsAsync(a => a.TryClaimAsync(Guid.NewGuid(), default));

        Assert.Equal(high.Id, first!.JobId);
        Assert.Equal(low.Id, second!.JobId);
        Assert.Null(third); // acme already has 1 running: low2 waits
        Assert.NotEqual(low2.Id, second.JobId);
    }

    [Fact]
    public async Task Schedules_fire_per_misfire_policy_and_never_twice_for_one_time()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var template = new SubmitJobRequest(null, null, [Step("a")]);

        async Task<Guid> Create(string name, string policy)
        {
            var definition = await host.WithAsync<ScheduleService, Domain.Entities.JobDefinition>(s =>
                s.CreateAsync(new CreateScheduleRequest(name, "*/5 * * * *", "UTC", template, policy), default));

            // Pretend the scheduler was down for the last 20 minutes.
            var due = DateTimeOffset.UtcNow.AddMinutes(-20);
            due = new DateTimeOffset(due.Year, due.Month, due.Day, due.Hour, due.Minute - due.Minute % 5, 0, TimeSpan.Zero);
            await host.ExecuteSqlAsync($"""UPDATE "JobDefinitions" SET "NextFireAt" = '{due:O}' WHERE "Id" = '{definition.Id}'""");
            return definition.Id;
        }

        var fireOnce = await Create("fire-once", "FireOnce");
        var skip = await Create("skip", "Skip");
        var catchUp = await Create("catch-up", "CatchUp");

        var now = DateTimeOffset.UtcNow;
        await host.WithAsync<ScheduleService, bool>(async s => { await s.FireDueAsync(now, default); return true; });

        // Firing the same due times again (a crash before NextFireAt moved) creates nothing.
        await host.ExecuteSqlAsync($"""UPDATE "JobDefinitions" SET "NextFireAt" = "NextFireAt" - interval '20 minutes'""");
        await host.WithAsync<ScheduleService, bool>(async s => { await s.FireDueAsync(now, default); return true; });

        var runs = await host.WithAsync<RelayForgeDbContext, List<Domain.Entities.Job>>(db =>
            db.Jobs.Where(j => j.DefinitionId != null).ToListAsync());

        Assert.Single(runs, r => r.DefinitionId == fireOnce);
        Assert.InRange(runs.Count(r => r.DefinitionId == catchUp), 4, 5);
        Assert.InRange(runs.Count(r => r.DefinitionId == skip), 0, 1); // only if a 5-minute mark is under a minute old
        Assert.Equal(runs.Count, runs.Select(r => (r.DefinitionId, r.ScheduledFor)).Distinct().Count());
    }

    [Fact]
    public async Task Forbid_skips_a_run_while_the_previous_one_is_active()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 0);
        var definition = await host.WithAsync<ScheduleService, Domain.Entities.JobDefinition>(s =>
            s.CreateAsync(new CreateScheduleRequest("no-overlap", "0 * * * *", "UTC", new SubmitJobRequest(null, null, [Step("a")]), null, "Forbid"), default));

        var first = await host.PostAsync($"/api/schedules/{definition.Id}/trigger");
        var second = await host.PostAsync($"/api/schedules/{definition.Id}/trigger");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Retention_job_deletes_old_finished_runs_only()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 1);
        var old = await host.SubmitAsync(Step("a"), Step("b", "a"));
        await host.WaitForFinishAsync(old.Id);
        var recent = await host.SubmitAsync(Step("a"));
        await host.WaitForFinishAsync(recent.Id);
        await host.ExecuteSqlAsync($"""UPDATE "Jobs" SET "CompletedAt" = now() - interval '30 days' WHERE "Id" = '{old.Id}'""");

        var retention = await host.SubmitAsync(new SubmitJobTaskRequest("retention", "retention", Type: "retention",
            Payload: JsonSerializer.SerializeToElement(new { olderThanDays = 7, batchSize = 1 }, HandlerJson.Options)));
        var finished = await host.WaitForFinishAsync(retention.Id);

        Assert.Equal("Succeeded", finished.State);
        Assert.Equal(1, finished.Tasks.Single().Output!.Value.GetProperty("deleted").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await host.CreateClient().GetAsync($"/api/jobs/{old.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.CreateClient().GetAsync($"/api/jobs/{recent.Id}")).StatusCode);
    }

    [Fact]
    public async Task System_retention_schedule_is_seeded_and_cannot_be_deleted()
    {
        await using var host = new RelayForgeHost(_postgres.NewDatabase(), workers: 1);
        List<ScheduleResponse>? schedules = null;

        for (var i = 0; i < 50 && (schedules is null || schedules.Count == 0); i++)
        {
            await Task.Delay(100);
            schedules = await host.CreateClient().GetFromJsonAsync<List<ScheduleResponse>>("/api/schedules");
        }

        var retention = Assert.Single(schedules!);
        Assert.Equal(ScheduleService.RetentionScheduleName, retention.Name);
        Assert.True(retention.IsSystem);
        Assert.Equal(HttpStatusCode.Conflict, (await host.CreateClient().DeleteAsync($"/api/schedules/{retention.Id}")).StatusCode);
    }
}

public class ScheduleCalculatorTests
{
    [Fact]
    public void Spring_forward_time_that_does_not_exist_fires_once_after_the_jump()
    {
        // 2026-03-29, Europe/London: 01:00 -> 02:00. "30 1 * * *" doesn't exist that day.
        var next = ScheduleCalculator.Next("30 1 * * *", "Europe/London", new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), next); // 02:00 BST
        var after = ScheduleCalculator.Next("30 1 * * *", "Europe/London", next);
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 0, 30, 0, TimeSpan.Zero), after); // back to 01:30 BST
    }

    [Fact]
    public void Fall_back_time_that_happens_twice_fires_once()
    {
        // 2026-10-25, Europe/London: 02:00 BST -> 01:00 GMT. 01:30 happens twice.
        var first = ScheduleCalculator.Next("30 1 * * *", "Europe/London", new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero));
        var second = ScheduleCalculator.Next("30 1 * * *", "Europe/London", first);

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), first); // 01:30 BST
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 1, 30, 0, TimeSpan.Zero), second); // next day, 01:30 GMT
    }

    [Fact]
    public void Misfire_policies_choose_which_missed_times_run()
    {
        var due = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var now = due.AddMinutes(10).AddSeconds(30); // missed 00:00, 00:05, 00:10

        var fireOnce = ScheduleCalculator.DueRuns("*/5 * * * *", "UTC", MisfirePolicy.FireOnce, due, now);
        var skip = ScheduleCalculator.DueRuns("*/5 * * * *", "UTC", MisfirePolicy.Skip, due, now);
        var catchUp = ScheduleCalculator.DueRuns("*/5 * * * *", "UTC", MisfirePolicy.CatchUp, due, now);

        Assert.Equal([due.AddMinutes(10)], fireOnce.Runs);
        Assert.Equal([due.AddMinutes(10)], skip.Runs); // 00:10 is 30 s old: on time
        Assert.Equal([due, due.AddMinutes(5), due.AddMinutes(10)], catchUp.Runs);
        Assert.All(new[] { fireOnce, skip, catchUp }, r => Assert.Equal(due.AddMinutes(15), r.NextFireAt));

        var late = ScheduleCalculator.DueRuns("*/5 * * * *", "UTC", MisfirePolicy.Skip, due, due.AddMinutes(13));
        Assert.Empty(late.Runs);
    }
}
