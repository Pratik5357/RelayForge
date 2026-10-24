using Microsoft.AspNetCore.SignalR;
using RelayForge.Infrastructure.Execution;

namespace RelayForge.Api.Realtime;

/// <summary>
/// Turns "task X wrote log lines" notifications (from any worker process, via Postgres NOTIFY)
/// into a "taskLogs" push to the job's SignalR group. The push carries no lines: watching
/// clients fetch what's new from GET /api/tasks/{id}/logs, so a dropped push costs nothing.
/// With the Redis backplane and N API instances, every instance hears the NOTIFY and pushes, so
/// a client gets N hints per batch of lines; each just triggers a cheap refetch.
/// </summary>
public sealed class TaskLogRelay : IHostedService
{
    private readonly RelaySignals _signals;
    private readonly IHubContext<JobEventsHub> _hub;

    public TaskLogRelay(RelaySignals signals, IHubContext<JobEventsHub> hub)
    {
        _signals = signals;
        _hub = hub;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _signals.LogsWritten += Push;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _signals.LogsWritten -= Push;
        return Task.CompletedTask;
    }

    private void Push(Guid jobId, Guid taskId) =>
        _ = _hub.Clients.Group(jobId.ToString()).SendAsync("taskLogs", new { jobId, taskId });
}
