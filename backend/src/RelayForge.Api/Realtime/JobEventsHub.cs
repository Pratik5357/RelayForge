using Microsoft.AspNetCore.SignalR;

namespace RelayForge.Api.Realtime;

/// <summary>
/// Clients call Subscribe(jobId) to join a group named after that job's id, then receive a
/// "jobChanged" push (see JobChangedEvent) whenever that job or one of its tasks changes
/// state. No server-to-client RPC beyond that one push type.
/// </summary>
public class JobEventsHub : Hub
{
    public Task Subscribe(string jobId) => Groups.AddToGroupAsync(Context.ConnectionId, jobId);

    public Task Unsubscribe(string jobId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
}
