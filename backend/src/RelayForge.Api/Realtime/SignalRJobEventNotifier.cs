using Microsoft.AspNetCore.SignalR;
using RelayForge.Infrastructure.Execution;

namespace RelayForge.Api.Realtime;

/// <summary>
/// The real IJobEventNotifier, backed by JobEventsHub. Pushes to the group named after the
/// job id -- clients join that group via the hub's Subscribe(jobId) method.
/// </summary>
public class SignalRJobEventNotifier : IJobEventNotifier
{
    private readonly IHubContext<JobEventsHub> _hub;

    public SignalRJobEventNotifier(IHubContext<JobEventsHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyAsync(JobChangedEvent evt, CancellationToken cancellationToken) =>
        _hub.Clients.Group(evt.JobId.ToString()).SendAsync("jobChanged", evt, cancellationToken);
}
