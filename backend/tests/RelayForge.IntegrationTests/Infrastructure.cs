using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RelayForge.Api.Contracts;
using RelayForge.Handlers;
using RelayForge.Infrastructure.Execution;
using RelayForge.Infrastructure.Jobs;
using Testcontainers.PostgreSql;

namespace RelayForge.IntegrationTests;

/// <summary>One throwaway Postgres container for the whole test run; each test gets its own database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        // Every test host keeps a listener, leader-lock and pooled connections; dozens of hosts
        // over a full run exceed the default 100.
        .WithCommand("-c", "max_connections=1000")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A connection string for a fresh database; the host's migrate-on-startup creates it.</summary>
    public string NewDatabase() =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = $"relayforge_{Guid.NewGuid():N}",
        }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>Records every pushed event so tests can assert on what (and how often) was announced.</summary>
public sealed class RecordingNotifier : IJobEventNotifier
{
    public ConcurrentQueue<JobChangedEvent> Events { get; } = new();

    public Task NotifyAsync(JobChangedEvent evt, CancellationToken cancellationToken)
    {
        Events.Enqueue(evt);
        return Task.CompletedTask;
    }
}

/// <summary>Test handler: returns its dependencies' outputs, so tests can see what was passed in.</summary>
public sealed class EchoInputsHandler : ITaskHandler
{
    public string Type => "test.echo";

    public string? Validate(JsonElement payload) => null;

    public Task<TaskResult> ExecuteAsync(TaskContext context) =>
        Task.FromResult<TaskResult>(new TaskResult.Success(JsonSerializer.SerializeToElement(context.Inputs, HandlerJson.Options)));
}

/// <summary>
/// The real API host, migrations and background services included, pointed at one test
/// database. workers: 0 boots a host that runs no workers, sweep or scheduler, which lets a
/// test drive claims by hand. Extra settings override the test defaults.
/// </summary>
public sealed class RelayForgeHost : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly int _workers;
    private readonly Dictionary<string, string?> _settings;

    public RelayForgeHost(string connectionString, int workers, Dictionary<string, string?>? settings = null)
    {
        _connectionString = connectionString;
        _workers = workers;
        _settings = settings ?? new();
    }

    public RecordingNotifier Notifier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development", so appsettings.Development.json's local connection string isn't loaded.
        builder.UseEnvironment("Testing");

        var defaults = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _connectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["WorkerPool:RunInApi"] = _workers > 0 ? "true" : "false",
            ["WorkerPool:Concurrency"] = Math.Max(_workers, 1).ToString(),
            ["WorkerPool:PollIntervalMs"] = "100",
            ["WorkerPool:ShutdownGraceMs"] = "300",
            ["WorkerPool:CancelGraceMs"] = "300",
            ["Reliability:BaseBackoffMs"] = "50",
            ["Reliability:MaxBackoffMs"] = "200",
            ["Reliability:LeaseDurationMs"] = "3000",
            ["Reliability:SweepIntervalMs"] = "200",
        };

        foreach (var (key, value) in defaults.Concat(_settings))
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IJobEventNotifier>(_ => Notifier);
            services.AddTaskHandler<EchoInputsHandler>();
        });
    }

    public async Task<JobDetailResponse> SubmitAsync(params SubmitJobTaskRequest[] tasks) =>
        await SubmitAsync(new SubmitJobRequest("test", null, tasks));

    public async Task<JobDetailResponse> SubmitAsync(SubmitJobRequest request)
    {
        var response = await CreateClient().PostAsJsonAsync("/api/jobs", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JobDetailResponse>())!;
    }

    public async Task<JobDetailResponse> GetJobAsync(Guid id) =>
        (await CreateClient().GetFromJsonAsync<JobDetailResponse>($"/api/jobs/{id}"))!;

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null) =>
        body is null
            ? await CreateClient().PostAsync(path, null)
            : await CreateClient().PostAsJsonAsync(path, body);

    public Task<JobDetailResponse> WaitForFinishAsync(Guid id, TimeSpan? timeout = null) =>
        WaitForAsync(id, job => job.State is "Succeeded" or "Failed" or "PartiallyFailed" or "Cancelled", timeout);

    public async Task<JobDetailResponse> WaitForAsync(Guid id, Func<JobDetailResponse, bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));

        while (true)
        {
            var job = await GetJobAsync(id);

            if (condition(job))
            {
                return job;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Job {id} didn't reach the expected state; it is {job.State}: {JsonSerializer.Serialize(job.Tasks.Select(t => new { t.Key, t.State, t.AttemptCount }))}");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>Runs <paramref name="work"/> with a service from a fresh DI scope.</summary>
    public async Task<T> WithAsync<TService, T>(Func<TService, Task<T>> work)
        where TService : notnull
    {
        using var scope = Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public Task<T> WithAttemptsAsync<T>(Func<TaskAttemptService, Task<T>> work) => WithAsync(work);

    /// <summary>Runs one SQL statement against the test database.</summary>
    public async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static SubmitJobTaskRequest Step(string key, params string[] dependsOn) =>
        new(key, key, SimulatedDurationMs: 0, dependsOn);

    /// <summary>A "simulated" step with the given handler options.</summary>
    public static SubmitJobTaskRequest Simulated(string key, object options, string[]? dependsOn = null, int? maxAttempts = null, string? isolation = null, int? timeoutMs = null) =>
        new(key, key, DependsOn: dependsOn ?? [], MaxAttempts: maxAttempts, Type: "simulated",
            Payload: JsonSerializer.SerializeToElement(options, HandlerJson.Options), Isolation: isolation, TimeoutMs: timeoutMs);
}
