using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// A background loop that runs in exactly one process at a time (usually). Leadership is a
/// Postgres session-level advisory lock held on a dedicated connection: if the leader process
/// dies, Postgres closes its connection and releases the lock, and another instance takes over
/// within <see cref="RetryLeadershipInterval"/>.
///
/// This is an efficiency measure, not a correctness one. Everything a leader does is safe to do
/// twice (fenced writes, unique constraints), so two leaders overlapping briefly -- e.g. while
/// a network partition makes a lost connection look alive -- waste work but corrupt nothing.
/// </summary>
public abstract class LeaderService : BackgroundService
{
    private static readonly TimeSpan RetryLeadershipInterval = TimeSpan.FromSeconds(5);

    private readonly RelayForgeConnectionString _connectionString;
    private readonly long _lockKey;

    protected LeaderService(RelayForgeConnectionString connectionString, long lockKey, ILogger logger)
    {
        _connectionString = connectionString;
        _lockKey = lockKey;
        Logger = logger;
    }

    protected ILogger Logger { get; }

    /// <summary>True while this instance holds the lock. For tests and diagnostics.</summary>
    public bool IsLeader { get; private set; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>One unit of leader work. Exceptions are logged and the loop continues.</summary>
    protected abstract Task TickAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString.Value);
                await connection.OpenAsync(stoppingToken);

                if (await TryLockAsync(connection, stoppingToken))
                {
                    IsLeader = true;
                    Logger.LogInformation("{Service} is now the leader", GetType().Name);
                    await LeadAsync(connection, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "{Service} lost its leadership connection", GetType().Name);
            }
            finally
            {
                if (IsLeader)
                {
                    Logger.LogInformation("{Service} stepped down", GetType().Name);
                }

                IsLeader = false;
            }

            try
            {
                await Task.Delay(RetryLeadershipInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task LeadAsync(NpgsqlConnection connection, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                Logger.LogError(ex, "{Service} tick failed", GetType().Name);
            }

            await Task.Delay(Interval, stoppingToken);

            // Still holding the lock means still connected; a broken connection throws here
            // and drops us back to follower.
            await using var ping = new NpgsqlCommand("SELECT 1", connection);
            await ping.ExecuteScalarAsync(stoppingToken);
        }
    }

    private async Task<bool> TryLockAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", _lockKey);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
