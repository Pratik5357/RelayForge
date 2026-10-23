using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RelayForge.Handlers;

// RelayForge.Runner: runs one task attempt in its own process, for Process and Container
// isolation. Protocol with the worker (ProcessExecutor):
//   stdin   -- one TaskInvocation as JSON
//   stdout  -- log lines, then exactly one "##relayforge-result {json}" line with the TaskResult
//   stderr  -- log lines (shown as stderr)
// The worker kills this process (tree) on timeout or cancel, so cancellation here is just the
// process ending: SIGTERM from `docker stop` is honored through the token below.

const string ResultMarker = "##relayforge-result ";

var invocation = JsonSerializer.Deserialize<TaskInvocation>(await Console.In.ReadToEndAsync(), HandlerJson.Options);

if (invocation is null)
{
    await Console.Error.WriteLineAsync("No invocation on stdin.");
    return 2;
}

var services = new ServiceCollection().AddBuiltInTaskHandlers().BuildServiceProvider();
var handler = services.GetRequiredService<TaskHandlerRegistry>().Find(invocation.Type);

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();

TaskResult result;

if (handler is null)
{
    result = new TaskResult.Fatal($"This runner has no handler for task type '{invocation.Type}'.");
}
else
{
    try
    {
        result = await handler.ExecuteAsync(new TaskContext
        {
            Invocation = invocation,
            Log = new ConsoleLog(),
            Cancellation = stop.Token,
        });
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        result = new TaskResult.Retryable($"{ex.GetType().Name}: {ex.Message}");
    }
}

Console.Out.WriteLine(ResultMarker + JsonSerializer.Serialize(result, HandlerJson.Options));
await Console.Out.FlushAsync();
return 0;

sealed class ConsoleLog : ITaskLog
{
    private readonly object _gate = new();

    public void Write(string line, string stream = "stdout")
    {
        lock (_gate)
        {
            (stream == "stderr" ? Console.Error : Console.Out).WriteLine(line);
        }
    }
}
