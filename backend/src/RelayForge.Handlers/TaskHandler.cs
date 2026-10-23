using System.Text.Json;
using System.Text.Json.Serialization;

namespace RelayForge.Handlers;

/// <summary>
/// One kind of work a task can do, selected by the task's Type (e.g. "http.request"). Handlers
/// run inside a worker, or inside RelayForge.Runner when the task asks for process isolation,
/// so they must not depend on anything only the worker has (the database, DI scopes) unless
/// they set <see cref="InProcessOnly"/>.
/// </summary>
public interface ITaskHandler
{
    string Type { get; }

    /// <summary>True for handlers that need the worker's services (e.g. the database).</summary>
    bool InProcessOnly => false;

    /// <summary>Checks a payload at submit time. Returns an error message, or null if it's valid.</summary>
    string? Validate(JsonElement payload);

    Task<TaskResult> ExecuteAsync(TaskContext context);
}

/// <summary>
/// The outcome of one attempt. Retryable failures back off and try again; fatal ones end the
/// task as Failed straight away, because retrying them can't help (a 400, a bad payload).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Success), "success")]
[JsonDerivedType(typeof(Retryable), "retryable")]
[JsonDerivedType(typeof(Fatal), "fatal")]
public abstract record TaskResult
{
    public sealed record Success(JsonElement? Output = null) : TaskResult;

    /// <param name="RetryAfter">A minimum wait the remote side asked for (e.g. HTTP Retry-After).</param>
    public sealed record Retryable(string Error, TimeSpan? RetryAfter = null) : TaskResult;

    public sealed record Fatal(string Error) : TaskResult;
}

/// <summary>Everything a handler gets for one attempt.</summary>
public sealed class TaskContext
{
    public required TaskInvocation Invocation { get; init; }

    /// <summary>Lines written here show up in the task's live log panel.</summary>
    public required ITaskLog Log { get; init; }

    /// <summary>Fires on timeout, job cancel, lost lease or worker shutdown. Honor it.</summary>
    public required CancellationToken Cancellation { get; init; }

    /// <summary>Fault injection for the chaos demos. A no-op outside an in-process worker.</summary>
    public IChaos Chaos { get; init; } = NoChaos.Instance;

    public Guid JobId => Invocation.JobId;
    public Guid TaskId => Invocation.TaskId;
    public string Key => Invocation.Key;
    public int Attempt => Invocation.Attempt;

    /// <summary>
    /// Stable across every retry of this task. Send it to external systems (e.g. as an
    /// Idempotency-Key header) so a retried side effect is deduplicated by the receiver: that is
    /// how at-least-once execution becomes exactly-once effects.
    /// </summary>
    public Guid IdempotencyKey => Invocation.IdempotencyKey;

    public JsonElement Payload => Invocation.Payload;

    /// <summary>Outputs of this task's dependencies, by their keys.</summary>
    public IReadOnlyDictionary<string, JsonElement> Inputs => Invocation.Inputs;
}

/// <summary>The serializable part of a context: what a worker hands to an out-of-process runner.</summary>
public sealed record TaskInvocation(
    Guid JobId,
    Guid TaskId,
    string Key,
    string Type,
    int Attempt,
    Guid IdempotencyKey,
    JsonElement Payload,
    IReadOnlyDictionary<string, JsonElement> Inputs);

public interface ITaskLog
{
    void Write(string line, string stream = "stdout");
}

public interface IChaos
{
    /// <summary>Stops renewing this attempt's lease, as if the worker process had frozen or died.</summary>
    void StopHeartbeat();
}

public sealed class NoChaos : IChaos
{
    public static readonly NoChaos Instance = new();

    public void StopHeartbeat()
    {
    }
}

/// <summary>Thrown by the simulated handler to make the worker abandon an attempt without writing anything.</summary>
public sealed class SimulatedCrashException : Exception
{
    public SimulatedCrashException()
        : base("Simulated worker crash.")
    {
    }
}

public static class HandlerJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static JsonElement EmptyObject { get; } = JsonDocument.Parse("{}").RootElement.Clone();
}
