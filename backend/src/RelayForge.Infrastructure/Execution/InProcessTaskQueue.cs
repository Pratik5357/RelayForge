using System.Threading.Channels;

namespace RelayForge.Infrastructure.Execution;

public class InProcessTaskQueue
{
    private readonly Channel<TaskWorkItem> _channel = Channel.CreateUnbounded<TaskWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        });

    public ChannelReader<TaskWorkItem> Reader => _channel.Reader;

    public void Enqueue(TaskWorkItem item)
    {
        _channel.Writer.TryWrite(item);
    }
}
