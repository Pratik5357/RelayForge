using Microsoft.AspNetCore.SignalR;
using TaskScheduler.Infrastructure.Events;

namespace TaskScheduler.Api.Realtime;

public sealed class JobEventsHub : Hub
{
    public Task Subscribe(Guid jobId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, jobId.ToString());
}

public sealed class SignalRJobEventPublisher : IJobEventPublisher
{
    private readonly IHubContext<JobEventsHub> _hub;

    public SignalRJobEventPublisher(IHubContext<JobEventsHub> hub)
    {
        _hub = hub;
    }

    public Task PublishAsync(JobChangedEvent evt, CancellationToken cancellationToken) =>
        _hub.Clients.Group(evt.JobId.ToString()).SendAsync("jobChanged", evt, cancellationToken);
}
