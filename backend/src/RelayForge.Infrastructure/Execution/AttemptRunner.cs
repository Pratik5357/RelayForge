using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RelayForge.Domain.Enums;
using RelayForge.Handlers;
using RelayForge.Infrastructure.Harness;
using RelayForge.Infrastructure.Telemetry;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// Runs one claimed attempt from start to finish: announce it, run the handler with a timeout,
/// keep the lease alive with a heartbeat, stream its logs, then report the outcome through the
/// fenced <see cref="TaskAttemptService.CompleteAsync"/>.
///
/// The handler's token fires on timeout, job cancel (heartbeat or NOTIFY), lost lease, or the
/// end of the shutdown grace period; <see cref="AttemptControl.Reason"/> says which.
/// </summary>
public sealed class AttemptRunner
{
    private readonly RelayForgeDbContext _db;
    private readonly TaskAttemptService _attempts;
    private readonly TaskExecutorSelector _executors;
    private readonly RunningAttempts _running;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReliabilityOptions _reliability;
    private readonly HarnessOptions _harness;
    private readonly ILogger<AttemptRunner> _logger;

    public AttemptRunner(
        RelayForgeDbContext db,
        TaskAttemptService attempts,
        TaskExecutorSelector executors,
        RunningAttempts running,
        IServiceScopeFactory scopeFactory,
        IOptions<ReliabilityOptions> reliability,
        IOptions<HarnessOptions> harness,
        ILogger<AttemptRunner> logger)
    {
        _db = db;
        _attempts = attempts;
        _executors = executors;
        _running = running;
        _scopeFactory = scopeFactory;
        _reliability = reliability.Value;
        _harness = harness.Value;
        _logger = logger;
    }

    /// <param name="shutdown">Fires when the worker's shutdown grace period ends.</param>
    public async Task RunAsync(ClaimedTask claim, Guid workerId, CancellationToken shutdown)
    {
        // Post-claim writes use CancellationToken.None: a worker that is shutting down must
        // still be able to report (or hand back) the attempt it holds.
        await _attempts.MarkStartedAsync(claim, workerId, CancellationToken.None);

        var task = await _db.JobTasks.AsNoTracking()
            .Include(t => t.Dependencies).ThenInclude(d => d.DependsOnJobTask)
            .FirstAsync(t => t.Id == claim.Id);

        var inputs = task.Dependencies
            .Where(d => d.DependsOnJobTask?.Output is not null)
            .ToDictionary(d => d.DependsOnJobTask!.Key, d => JsonDocument.Parse(d.DependsOnJobTask!.Output!).RootElement.Clone());

        var invocation = new TaskInvocation(
            task.JobId,
            task.Id,
            task.Key,
            task.Type,
            claim.AttemptCount,
            task.IdempotencyKey,
            JsonDocument.Parse(task.Payload).RootElement.Clone(),
            inputs);

        var timeout = TimeSpan.FromMilliseconds(task.TimeoutMs > 0 ? task.TimeoutMs : _reliability.DefaultTimeoutMs);

        // Queue -> worker: a database table carries no trace context, so the submit's
        // traceparent is stored on the job and becomes this span's parent.
        ActivityContext.TryParse(claim.TraceParent, null, out var parent);
        using var activity = RelayForgeTelemetry.Source.StartActivity("task.execute", ActivityKind.Consumer, parent);
        activity?.SetTag("relayforge.job.id", task.JobId);
        activity?.SetTag("relayforge.task.key", task.Key);
        activity?.SetTag("relayforge.task.type", task.Type);
        activity?.SetTag("relayforge.task.attempt", claim.AttemptCount);
        activity?.SetTag("relayforge.task.isolation", task.Isolation.ToString());

        using var control = new AttemptControl(task.JobId, timeout, shutdown);
        using var tracked = _running.Track(control);
        using var heartbeatCts = new CancellationTokenSource();
        var heartbeat = HeartbeatAsync(claim, workerId, control, heartbeatCts.Token);

        AttemptReport? report;

        await using (var log = new TaskLogSink(_scopeFactory, _logger, task.JobId, task.Id, claim.AttemptCount, _harness.MaxLogLinesPerAttempt))
        {
            var chaos = new HeartbeatChaos(heartbeatCts);

            try
            {
                var result = await _executors.For(task.Isolation)
                    .ExecuteAsync(new ExecutionRequest(invocation, task.Isolation, log, chaos), control.Token);
                report = ToReport(result);
            }
            catch (SimulatedCrashException)
            {
                // The worker "dies": no heartbeat, no write. Only the lease expiring recovers it.
                _logger.LogWarning("Simulated crash: abandoning attempt {Attempt} of task {TaskId}", claim.AttemptCount, claim.Id);
                report = null;
            }
            catch (OperationCanceledException) when (control.Token.IsCancellationRequested)
            {
                report = control.Reason switch
                {
                    StopReason.Timeout => new AttemptReport(AttemptOutcome.Failed, $"Timed out after {timeout.TotalSeconds:0.#} s."),
                    StopReason.JobCancelled => new AttemptReport(AttemptOutcome.Cancelled, "Stopped: the job was cancelled while this attempt was running."),
                    StopReason.Shutdown => new AttemptReport(AttemptOutcome.Released, "The worker shut down before this attempt finished."),

                    // Someone else owns the task now; there's nothing this worker may write.
                    _ => null,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attempt {Attempt} of task {TaskId} threw", claim.AttemptCount, claim.Id);
                log.Write($"Unhandled {ex.GetType().Name}: {ex.Message}", "stderr");
                report = new AttemptReport(AttemptOutcome.Failed, $"{ex.GetType().Name}: {ex.Message}");
            }

            if (report is not null && report.Outcome != AttemptOutcome.Succeeded)
            {
                log.Write($"Attempt {claim.AttemptCount} ended: {report.Outcome}{(report.Error is null ? "" : $" -- {report.Error}")}", "stderr");
            }
        }

        heartbeatCts.Cancel();
        await heartbeat;

        activity?.SetTag("relayforge.task.outcome", report?.Outcome.ToString() ?? "Abandoned");

        if (report is null)
        {
            return;
        }

        if (report.Outcome != AttemptOutcome.Succeeded)
        {
            activity?.SetStatus(ActivityStatusCode.Error, report.Error);
        }

        await _attempts.CompleteAsync(claim, workerId, report, CancellationToken.None);
    }

    private static AttemptReport ToReport(TaskResult result) => result switch
    {
        TaskResult.Success s => new AttemptReport(AttemptOutcome.Succeeded, OutputJson: s.Output?.GetRawText()),
        TaskResult.Retryable r => new AttemptReport(AttemptOutcome.Failed, r.Error, RetryAfter: r.RetryAfter),
        TaskResult.Fatal f => new AttemptReport(AttemptOutcome.Fatal, f.Error),
        _ => new AttemptReport(AttemptOutcome.Failed, "The handler returned an unknown result."),
    };

    /// <summary>
    /// Renews the lease every third of its length, in its own DbContext (a context isn't
    /// thread-safe, and the handler's side may be using this scope's). A lost lease stops the
    /// handler -- its result would be rejected anyway. A cancel request stops it too, but keeps
    /// renewing so the attempt can wind down without being reclaimed.
    /// </summary>
    private async Task HeartbeatAsync(ClaimedTask claim, Guid workerId, AttemptControl control, CancellationToken stop)
    {
        using var scope = _scopeFactory.CreateScope();
        var attempts = scope.ServiceProvider.GetRequiredService<TaskAttemptService>();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(50, _reliability.LeaseDurationMs / 3)));

        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    switch (await attempts.RenewLeaseAsync(claim, workerId, stop))
                    {
                        case LeaseRenewal.Lost:
                            _logger.LogWarning("Lost the lease on attempt {Attempt} of task {TaskId}; stopping it", claim.AttemptCount, claim.Id);
                            control.Stop(StopReason.LeaseLost);
                            return;
                        case LeaseRenewal.CancelRequested:
                            control.Stop(StopReason.JobCancelled);
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One failed renewal isn't fatal: the lease has two more renewals of slack.
                    _logger.LogWarning(ex, "Heartbeat for task {TaskId} failed", claim.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Attempt over, or heartbeat deliberately stopped by the chaos demo.
        }
    }

    private sealed class HeartbeatChaos(CancellationTokenSource heartbeat) : IChaos
    {
        public void StopHeartbeat() => heartbeat.Cancel();
    }
}
