using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace RelayForge.Infrastructure.Execution;

/// <summary>
/// Holds one connection that LISTENs on RelayForge's NOTIFY channels and turns each
/// notification into a local signal. NOTIFY is delivered on commit, to every listening
/// process, which is what lets separate API and worker processes wake each other instantly.
/// Notifications sent while this connection is down are lost; every loop also polls, so that
/// costs latency, never correctness.
/// </summary>
public class PostgresListener : BackgroundService
{
    private readonly RelayForgeConnectionString _connectionString;
    private readonly RelaySignals _signals;
    private readonly ILogger<PostgresListener> _logger;

    public PostgresListener(RelayForgeConnectionString connectionString, RelaySignals signals, ILogger<PostgresListener> logger)
    {
        _connectionString = connectionString;
        _signals = signals;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString.Value);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, e) => Route(e.Channel, e.Payload);

                await using (var listen = new NpgsqlCommand(
                    $"LISTEN {PgChannels.Ready}; LISTEN {PgChannels.Outbox}; LISTEN {PgChannels.Cancel}; LISTEN {PgChannels.Logs};",
                    connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                // Anything committed while we weren't listening: look once.
                _signals.Work.Notify();
                _signals.Outbox.Notify();

                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification listener lost its connection; reconnecting");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void Route(string channel, string payload)
    {
        switch (channel)
        {
            case PgChannels.Ready:
                _signals.Work.Notify();
                break;
            case PgChannels.Outbox:
                _signals.Outbox.Notify();
                break;
            case PgChannels.Cancel when Guid.TryParse(payload, out var jobId):
                _signals.RaiseJobCancelRequested(jobId);
                break;
            case PgChannels.Logs:
                var parts = payload.Split(':');
                if (parts.Length == 2 && Guid.TryParse(parts[0], out var logJob) && Guid.TryParse(parts[1], out var logTask))
                {
                    _signals.RaiseLogsWritten(logJob, logTask);
                }

                break;
        }
    }
}

/// <summary>The resolved database connection string, for code that talks to Npgsql directly.</summary>
public sealed record RelayForgeConnectionString(string Value);
