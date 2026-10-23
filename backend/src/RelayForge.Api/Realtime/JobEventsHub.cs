using Microsoft.AspNetCore.SignalR;

namespace RelayForge.Api.Realtime;

/// <summary>
/// Clients call Subscribe(jobId) to join a group named after that job's id, then receive a
/// "jobChanged" push (see JobChangedEvent, delivered from the outbox: at-least-once, numbered
/// per job) whenever that job or one of its tasks changes, and a "taskLogs" push when a task
/// writes log lines.
/// </summary>
public class JobEventsHub : Hub
{
    public Task Subscribe(string jobId) => Groups.AddToGroupAsync(Context.ConnectionId, jobId);

    public Task Unsubscribe(string jobId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
}
