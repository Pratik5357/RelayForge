using System.Text.Json;

namespace TaskScheduler.Infrastructure.Handlers;

public interface ITaskHandler
{
    string Type { get; }
    Task<string> ExecuteAsync(string payloadJson, int attemptCount, CancellationToken cancellationToken);
}

public sealed class EchoHandler : ITaskHandler
{
    public string Type => "echo";

    public Task<string> ExecuteAsync(string payloadJson, int attemptCount, CancellationToken cancellationToken)
        => Task.FromResult(payloadJson);
}

public sealed class DelayHandler : ITaskHandler
{
    public string Type => "delay";

    public async Task<string> ExecuteAsync(string payloadJson, int attemptCount, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
        var ms = 50;
        if (doc.RootElement.TryGetProperty("milliseconds", out var prop) && prop.TryGetInt32(out var parsed))
        {
            ms = Math.Clamp(parsed, 0, 60_000);
        }

        await Task.Delay(ms, cancellationToken);
        return payloadJson;
    }
}

public sealed class FailHandler : ITaskHandler
{
    public string Type => "fail";

    public Task<string> ExecuteAsync(string payloadJson, int attemptCount, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
        if (doc.RootElement.TryGetProperty("succeedOnAttempt", out var prop) && prop.TryGetInt32(out var succeedOn)
            && attemptCount >= succeedOn)
        {
            return Task.FromResult("""{"status":"recovered"}""");
        }

        throw new InvalidOperationException($"Intentional failure on attempt {attemptCount}.");
    }
}
