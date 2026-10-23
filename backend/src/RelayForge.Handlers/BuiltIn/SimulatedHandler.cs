using System.Text.Json;

namespace RelayForge.Handlers.BuiltIn;

/// <summary>
/// "simulated": does no real work. It waits for durationMs, writing a progress line each
/// second, then succeeds or fails on schedule. Its options drive every demo scenario:
///   failUntilAttempt  -- retryable failure on attempts 1..N-1 (above maxAttempts = dead-letters)
///   fatalOnAttempt    -- a non-retryable failure on that attempt (task ends Failed)
///   crashOnAttempt    -- the worker goes silent: heartbeat stops, nothing is written
///   pauseOnAttempt    -- a "zombie": heartbeat stops, the attempt freezes for pauseMs, then
///                        reports success anyway (the fence must reject it)
///   ignoreCancellation -- a runaway: keeps going after timeout or cancel; only a process
///                        harness can stop it
///   output            -- returned as the task's output (default: { "attempt": n })
/// </summary>
public sealed class SimulatedHandler : ITaskHandler
{
    public string Type => "simulated";

    public sealed record Options(
        int DurationMs = 0,
        int? FailUntilAttempt = null,
        int? FatalOnAttempt = null,
        int? CrashOnAttempt = null,
        int? PauseOnAttempt = null,
        int PauseMs = 0,
        bool IgnoreCancellation = false,
        JsonElement? Output = null);

    public string? Validate(JsonElement payload)
    {
        Options? options;

        try
        {
            options = payload.Deserialize<Options>(HandlerJson.Options);
        }
        catch (JsonException ex)
        {
            return $"Invalid simulated payload: {ex.Message}";
        }

        if (options is null)
        {
            return "The simulated payload must be an object.";
        }

        if (options.DurationMs < 0 || options.PauseMs < 0)
        {
            return "durationMs and pauseMs can't be negative.";
        }

        return null;
    }

    public async Task<TaskResult> ExecuteAsync(TaskContext context)
    {
        var options = context.Payload.Deserialize<Options>(HandlerJson.Options) ?? new Options();
        var attempt = context.Attempt;

        context.Log.Write($"Attempt {attempt}: working for {options.DurationMs} ms.");

        if (options.CrashOnAttempt == attempt)
        {
            context.Log.Write("Simulating a crash: the worker stops heartbeating and abandons this attempt.", "stderr");
            context.Chaos.StopHeartbeat();
            throw new SimulatedCrashException();
        }

        if (options.PauseOnAttempt == attempt)
        {
            context.Log.Write($"Simulating a {options.PauseMs} ms freeze (GC pause, stuck I/O). The lease will run out.", "stderr");
            context.Chaos.StopHeartbeat();

            // A frozen process notices nothing, cancellation included.
            await Task.Delay(options.PauseMs, CancellationToken.None);
            context.Log.Write("Woke up. Reporting success as if nothing happened.");
            return new TaskResult.Success(OutputFor(options, attempt));
        }

        var token = options.IgnoreCancellation ? CancellationToken.None : context.Cancellation;
        var remaining = options.DurationMs;
        var second = 0;

        while (remaining > 0)
        {
            var step = Math.Min(remaining, 1000);
            await Task.Delay(step, token);
            remaining -= step;
            second++;

            if (remaining > 0)
            {
                context.Log.Write($"Still working ({second} s).");
            }
        }

        if (options.FatalOnAttempt == attempt)
        {
            return new TaskResult.Fatal($"Simulated non-retryable failure on attempt {attempt}.");
        }

        if (options.FailUntilAttempt is { } until && attempt < until)
        {
            return new TaskResult.Retryable(
                $"Simulated failure on attempt {attempt} (this task succeeds starting attempt {until}).");
        }

        context.Log.Write("Done.");
        return new TaskResult.Success(OutputFor(options, attempt));
    }

    private static JsonElement OutputFor(Options options, int attempt) =>
        options.Output ?? JsonSerializer.SerializeToElement(new { attempt }, HandlerJson.Options);
}
