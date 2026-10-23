using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RelayForge.Domain.Entities;
using RelayForge.Handlers;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// Collects one attempt's log lines and writes them to TaskLogs in small batches (every 300 ms),
/// then NOTIFYs so the API can tell watching browsers to fetch them. Stores at most
/// MaxLogLinesPerAttempt lines; past that, one note and the rest are dropped.
/// </summary>
public sealed class TaskLogSink : ITaskLog, IAsyncDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(300);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly Guid _jobId;
    private readonly Guid _taskId;
    private readonly int _attempt;
    private readonly int _maxLines;
    private readonly ConcurrentQueue<TaskLogLine> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _flushLoop;
    private int _sequence;

    public TaskLogSink(IServiceScopeFactory scopeFactory, ILogger logger, Guid jobId, Guid taskId, int attempt, int maxLines)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _jobId = jobId;
        _taskId = taskId;
        _attempt = attempt;
        _maxLines = maxLines;
        _flushLoop = FlushLoopAsync();
    }

    public void Write(string line, string stream = "stdout")
    {
        var sequence = Interlocked.Increment(ref _sequence);

        if (sequence > _maxLines + 1)
        {
            return;
        }

        if (sequence == _maxLines + 1)
        {
            line = $"[log truncated: more than {_maxLines} lines; the rest were dropped]";
            stream = "stderr";
        }

        _pending.Enqueue(new TaskLogLine
        {
            JobId = _jobId,
            TaskId = _taskId,
            Attempt = _attempt,
            Sequence = sequence,
            Stream = stream,
            Line = line.Length > 4000 ? line[..4000] : line,
            At = DateTimeOffset.UtcNow,
        });
    }

    private async Task FlushLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FlushAsync();
        }
    }

    private async Task FlushAsync()
    {
        var batch = new List<TaskLogLine>();

        while (_pending.TryDequeue(out var line))
        {
            batch.Add(line);
        }

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RelayForgeDbContext>();
            db.TaskLogs.AddRange(batch);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_notify({PgChannels.Logs}, {$"{_jobId}:{_taskId}"})");
        }
        catch (Exception ex)
        {
            // Logs are best-effort; never let them fail an attempt.
            _logger.LogWarning(ex, "Could not store {Count} log lines for task {TaskId}", batch.Count, _taskId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await _flushLoop;
        await FlushAsync();
        _stop.Dispose();
    }
}
