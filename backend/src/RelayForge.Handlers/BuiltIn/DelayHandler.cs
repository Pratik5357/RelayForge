using System.Text.Json;

namespace RelayForge.Handlers.BuiltIn;

/// <summary>"delay": waits for ms milliseconds, then succeeds. Payload: { "ms": 5000 }.</summary>
public sealed class DelayHandler : ITaskHandler
{
    public string Type => "delay";

    private sealed record Options(int Ms);

    public string? Validate(JsonElement payload)
    {
        try
        {
            var options = payload.Deserialize<Options>(HandlerJson.Options);
            return options is null || options.Ms < 0 ? "delay needs a non-negative \"ms\"." : null;
        }
        catch (JsonException ex)
        {
            return $"Invalid delay payload: {ex.Message}";
        }
    }

    public async Task<TaskResult> ExecuteAsync(TaskContext context)
    {
        var options = context.Payload.Deserialize<Options>(HandlerJson.Options)!;
        context.Log.Write($"Waiting {options.Ms} ms.");
        await Task.Delay(options.Ms, context.Cancellation);
        return new TaskResult.Success();
    }
}
