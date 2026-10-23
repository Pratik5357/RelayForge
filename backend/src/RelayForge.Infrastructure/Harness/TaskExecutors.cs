using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RelayForge.Domain.Enums;
using RelayForge.Handlers;
using RelayForge.Infrastructure.Execution;

namespace RelayForge.Infrastructure.Harness;

/// <summary>What a worker hands to an executor for one attempt.</summary>
public sealed record ExecutionRequest(TaskInvocation Invocation, TaskIsolation Isolation, ITaskLog Log, IChaos Chaos);

/// <summary>
/// Runs one attempt's handler somewhere. Throws OperationCanceledException when the token
/// fires (timeout, job cancel, lease loss, shutdown); the worker knows which of those it was.
/// </summary>
public interface ITaskExecutor
{
    Task<TaskResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken);
}

/// <summary>Picks the executor for the task's isolation level.</summary>
public sealed class TaskExecutorSelector
{
    private readonly InProcessExecutor _inProcess;
    private readonly ProcessExecutor _process;

    public TaskExecutorSelector(InProcessExecutor inProcess, ProcessExecutor process)
    {
        _inProcess = inProcess;
        _process = process;
    }

    public ITaskExecutor For(TaskIsolation isolation) =>
        isolation == TaskIsolation.InProcess ? _inProcess : _process;
}

/// <summary>
/// Runs the handler on a worker thread. Cheap, but it can only ask a handler to stop: .NET has
/// no safe way to kill a thread. A handler that ignores its token gets CancelGraceMs, then the
/// worker stops waiting for it and moves on, while the handler keeps running in the background
/// until it finishes on its own. Only process isolation can force-stop it.
/// </summary>
public sealed class InProcessExecutor : ITaskExecutor
{
    private readonly TaskHandlerRegistry _handlers;
    private readonly WorkerOptions _options;
    private readonly ILogger<InProcessExecutor> _logger;

    public InProcessExecutor(TaskHandlerRegistry handlers, IOptions<WorkerOptions> options, ILogger<InProcessExecutor> logger)
    {
        _handlers = handlers;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TaskResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        var handler = _handlers.Find(request.Invocation.Type);

        if (handler is null)
        {
            return new TaskResult.Fatal($"No handler is registered for task type '{request.Invocation.Type}'.");
        }

        var context = new TaskContext
        {
            Invocation = request.Invocation,
            Log = request.Log,
            Cancellation = cancellationToken,
            Chaos = request.Chaos,
        };

        // Task.Run: a handler that blocks synchronously must not block the worker loop.
        var run = Task.Run(() => handler.ExecuteAsync(context), CancellationToken.None);

        using var stopWaiting = new CancellationTokenSource();
        var giveUp = WhenCancelledPlusGraceAsync(cancellationToken, TimeSpan.FromMilliseconds(_options.CancelGraceMs), stopWaiting.Token);

        try
        {
            if (await Task.WhenAny(run, giveUp) != run)
            {
                _logger.LogWarning(
                    "Handler {Type} for task {TaskId} ignored cancellation; abandoning it (it keeps running in the background)",
                    request.Invocation.Type, request.Invocation.TaskId);
                request.Log.Write(
                    "The handler ignored cancellation and is still running in the background. Use process isolation to force-stop handlers like this.",
                    "stderr");
                _ = run.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                throw new OperationCanceledException(cancellationToken);
            }

            return await run;
        }
        finally
        {
            stopWaiting.Cancel();
        }
    }

    /// <summary>Completes CancelGraceMs after <paramref name="cancelled"/> fires; <paramref name="stop"/> ends the wait early.</summary>
    private static async Task WhenCancelledPlusGraceAsync(CancellationToken cancelled, TimeSpan grace, CancellationToken stop)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(cancelled, stop);

        try
        {
            await Task.Delay(Timeout.Infinite, either.Token);
        }
        catch (OperationCanceledException)
        {
        }

        if (stop.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await Task.Delay(grace, stop);
        }
        catch (OperationCanceledException)
        {
        }
    }
}

/// <summary>
/// Runs the handler in a separate RelayForge.Runner process -- directly (Process isolation) or
/// in a throwaway container with memory, CPU and network limits (Container isolation). The
/// invocation goes in on stdin; every stdout/stderr line is a live log line; the result comes
/// back as one marked stdout line. On timeout or cancel the whole process tree is killed, which
/// is the only way to stop code that ignores cancellation.
/// </summary>
public sealed class ProcessExecutor : ITaskExecutor
{
    public const string ResultMarker = "##relayforge-result ";

    private readonly HarnessOptions _options;
    private readonly ILogger<ProcessExecutor> _logger;

    public ProcessExecutor(IOptions<HarnessOptions> options, ILogger<ProcessExecutor> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TaskResult> ExecuteAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        var invocation = request.Invocation;
        var containerName = $"relayforge-{invocation.TaskId:N}-{invocation.Attempt}";
        var psi = request.Isolation == TaskIsolation.Container
            ? ContainerStartInfo(invocation, containerName)
            : RunnerStartInfo();

        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;

        TaskResult? result = null;
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            if (e.Data.StartsWith(ResultMarker, StringComparison.Ordinal))
            {
                try
                {
                    result = JsonSerializer.Deserialize<TaskResult>(e.Data[ResultMarker.Length..], HandlerJson.Options);
                }
                catch (JsonException ex)
                {
                    request.Log.Write($"Runner sent an unreadable result: {ex.Message}", "stderr");
                }

                return;
            }

            request.Log.Write(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                request.Log.Write(e.Data, "stderr");
            }
        };

        if (!process.Start())
        {
            return new TaskResult.Retryable($"Could not start {psi.FileName}.");
        }

        request.Log.Write($"Started {(request.Isolation == TaskIsolation.Container ? $"container {containerName}" : $"runner process {process.Id}")}.", "stderr");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(invocation, HandlerJson.Options));
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The hard stop in-process execution can't do.
            KillQuietly(process);

            if (request.Isolation == TaskIsolation.Container)
            {
                await KillContainerAsync(containerName);
            }

            request.Log.Write("Killed the runner (timeout or cancel).", "stderr");
            throw;
        }

        // WaitForExitAsync returns once the process exits; the final output events can trail it.
        process.WaitForExit();

        return result ?? new TaskResult.Retryable($"The runner exited with code {process.ExitCode} without reporting a result.");
    }

    private ProcessStartInfo RunnerStartInfo()
    {
        var runner = _options.RunnerPath ?? Path.Combine(AppContext.BaseDirectory, "RelayForge.Runner.dll");
        var psi = new ProcessStartInfo(DotnetHost());
        psi.ArgumentList.Add(runner);
        return psi;
    }

    private ProcessStartInfo ContainerStartInfo(TaskInvocation invocation, string name)
    {
        var psi = new ProcessStartInfo("docker");

        foreach (var arg in new[]
                 {
                     "run", "--rm", "-i", "--name", name,
                     "--memory", $"{_options.ContainerMemoryMb}m",
                     "--cpus", _options.ContainerCpus.ToString(CultureInfo.InvariantCulture),
                     "--read-only",
                     // Only tasks that call out get a network.
                     "--network", invocation.Type == "http.request" ? "bridge" : "none",
                     _options.ContainerImage,
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    /// <summary>The dotnet host to start the runner with: the SDK's when under `dotnet test`, else PATH's.</summary>
    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";

    private void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogDebug(ex, "Runner had already exited");
        }
    }

    private static async Task KillContainerAsync(string name)
    {
        try
        {
            using var kill = Process.Start(new ProcessStartInfo("docker", $"kill {name}") { RedirectStandardOutput = true, RedirectStandardError = true });

            if (kill is not null)
            {
                await kill.WaitForExitAsync();
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No docker CLI: nothing more we can do.
        }
    }
}
