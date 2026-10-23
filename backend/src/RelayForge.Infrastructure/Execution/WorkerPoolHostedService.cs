using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// WorkerPool:Concurrency consumer loops, each claiming Ready tasks straight from the database.
/// An idle consumer sleeps until a ready NOTIFY arrives or WorkerPool:PollIntervalMs passes;
/// the poll is what picks up retries whose backoff elapsed.
///
/// Graceful shutdown: when the host stops, consumers stop claiming at once, in-flight attempts
/// get WorkerPool:ShutdownGraceMs to finish, and whatever is still running then is handed back
/// to Ready without spending a retry. A hard kill skips all this; the lease expiring covers it.
/// </summary>
public class WorkerPoolHostedService : BackgroundService
{
    private readonly RelaySignals _signals;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkerPoolHostedService> _logger;
    private readonly WorkerOptions _options;
    private readonly CancellationTokenSource _graceEnded = new();

    public WorkerPoolHostedService(
        RelaySignals signals,
        IServiceScopeFactory scopeFactory,
        IOptions<WorkerOptions> options,
        ILogger<WorkerPoolHostedService> logger)
    {
        _signals = signals;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var startGrace = stoppingToken.Register(() =>
        {
            _logger.LogInformation("Worker pool stopping: no new claims; in-flight attempts have {GraceMs} ms", _options.ShutdownGraceMs);
            _graceEnded.CancelAfter(_options.ShutdownGraceMs);
        });

        var consumers = Enumerable.Range(0, _options.Concurrency)
            .Select(workerIndex => ConsumeAsync(workerIndex, stoppingToken));

        await Task.WhenAll(consumers);
    }

    private async Task ConsumeAsync(int workerIndex, CancellationToken stoppingToken)
    {
        // Stable for this consumer loop's lifetime -- recorded as JobTask.LeaseOwnerId, and part
        // of the fence every post-claim write checks.
        var workerId = Guid.NewGuid();
        var pollInterval = TimeSpan.FromMilliseconds(_options.PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            var worked = false;

            try
            {
                worked = await ClaimAndRunOneAsync(workerId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker {WorkerIndex} failed while claiming or running a task", workerIndex);
            }

            // Straight back to the queue after real work; otherwise wait for a signal or the poll.
            if (!worked)
            {
                try
                {
                    await _signals.Work.WaitAsync(pollInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    private async Task<bool> ClaimAndRunOneAsync(Guid workerId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var attempts = scope.ServiceProvider.GetRequiredService<TaskAttemptService>();

        var claim = await attempts.TryClaimAsync(workerId, stoppingToken);

        if (claim is null)
        {
            return false;
        }

        await scope.ServiceProvider.GetRequiredService<AttemptRunner>().RunAsync(claim, workerId, _graceEnded.Token);
        return true;
    }

    public override void Dispose()
    {
        _graceEnded.Dispose();
        base.Dispose();
    }
}
