using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RelayForge.Domain.Entities;
using RelayForge.Domain.Enums;
using RelayForge.Handlers;
using RelayForge.Infrastructure.Jobs;
using RelayForge.Infrastructure.Telemetry;

namespace RelayForge.Infrastructure.Scheduling;

public record CreateScheduleRequest(
    string Name,
    string Cron,
    string? TimeZone,
    SubmitJobRequest Template,
    string? MisfirePolicy = null,
    string? ConcurrencyPolicy = null,
    bool Enabled = true);

/// <summary>Creates, changes and fires recurring jobs.</summary>
public class ScheduleService
{
    public const string RetentionScheduleName = "relayforge.retention";

    private static readonly JobState[] ActiveStates = [JobState.Pending, JobState.Running];

    private readonly RelayForgeDbContext _db;
    private readonly JobSubmissionService _submissions;
    private readonly JobOperations _operations;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScheduleService> _logger;

    public ScheduleService(
        RelayForgeDbContext db,
        JobSubmissionService submissions,
        JobOperations operations,
        IConfiguration configuration,
        ILogger<ScheduleService> logger)
    {
        _db = db;
        _submissions = submissions;
        _operations = operations;
        _configuration = configuration;
        _logger = logger;
    }

    /// <exception cref="JobValidationException">Bad cron, zone, policy, name or template.</exception>
    public async Task<JobDefinition> CreateAsync(CreateScheduleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
        {
            throw Invalid("A schedule needs a name of at most 200 characters.");
        }

        if (request.Name.StartsWith("relayforge.", StringComparison.Ordinal))
        {
            throw Invalid("Names starting with 'relayforge.' are reserved.");
        }

        var timeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "UTC" : request.TimeZone;

        if (ScheduleCalculator.Validate(request.Cron, timeZone) is { } error)
        {
            throw Invalid(error);
        }

        var misfire = ParsePolicy(request.MisfirePolicy, MisfirePolicy.FireOnce);
        var concurrency = ParsePolicy(request.ConcurrencyPolicy, ConcurrencyPolicy.Allow);

        // Same checks a submit makes, now rather than at 2 a.m.
        _submissions.ValidateOnly(request.Template);

        if (await _db.JobDefinitions.AnyAsync(d => d.Name == request.Name, cancellationToken))
        {
            throw Invalid($"A schedule named '{request.Name}' already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var definition = new JobDefinition
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Cron = request.Cron,
            TimeZone = timeZone,
            Template = JsonSerializer.Serialize(request.Template, HandlerJson.Options),
            MisfirePolicy = misfire,
            ConcurrencyPolicy = concurrency,
            Enabled = request.Enabled,
            NextFireAt = ScheduleCalculator.Next(request.Cron, timeZone, now),
            CreatedAt = now,
        };

        _db.JobDefinitions.Add(definition);
        await _db.SaveChangesAsync(cancellationToken);
        return definition;
    }

    /// <summary>Enables or disables. Re-enabling starts from the next time after now, so it doesn't count as a misfire.</summary>
    public async Task<JobDefinition?> SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var definition = await _db.JobDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (definition is null)
        {
            return null;
        }

        if (enabled && !definition.Enabled)
        {
            definition.NextFireAt = ScheduleCalculator.Next(definition.Cron, definition.TimeZone, DateTimeOffset.UtcNow);
        }

        definition.Enabled = enabled;
        await _db.SaveChangesAsync(cancellationToken);
        return definition;
    }

    /// <returns>Null if not found; otherwise whether it was deleted (system schedules can't be).</returns>
    public async Task<bool?> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var definition = await _db.JobDefinitions.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        if (definition is null)
        {
            return null;
        }

        if (definition.IsSystem)
        {
            return false;
        }

        _db.JobDefinitions.Remove(definition);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Runs a schedule once now, outside its cron, still honoring its concurrency policy.</summary>
    public async Task<SubmitResult?> TriggerAsync(Guid id, CancellationToken cancellationToken)
    {
        var definition = await _db.JobDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return definition is null ? null : await FireAsync(definition, DateTimeOffset.UtcNow, manual: true, cancellationToken);
    }

    /// <summary>Fires every enabled schedule that is due, per its misfire policy, and moves it on.</summary>
    public async Task FireDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await _db.JobDefinitions.AsNoTracking()
            .Where(d => d.Enabled && d.NextFireAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var definition in due)
        {
            var (runs, next) = ScheduleCalculator.DueRuns(definition.Cron, definition.TimeZone, definition.MisfirePolicy, definition.NextFireAt, now);

            if (runs.Count == 0)
            {
                _logger.LogInformation("Schedule {Name} missed {Due:u}; misfire policy {Policy} skips it", definition.Name, definition.NextFireAt, definition.MisfirePolicy);
            }

            foreach (var scheduledFor in runs)
            {
                await FireAsync(definition, scheduledFor, manual: false, cancellationToken);
            }

            // Not a tracked save: submitting a run goes through JobOrchestrator, which clears
            // this context's change tracker.
            var lastFiredAt = runs.Count > 0 ? now : definition.LastFiredAt;
            await _db.JobDefinitions
                .Where(d => d.Id == definition.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.NextFireAt, next)
                    .SetProperty(d => d.LastFiredAt, lastFiredAt), cancellationToken);
        }
    }

    private async Task<SubmitResult?> FireAsync(JobDefinition definition, DateTimeOffset scheduledFor, bool manual, CancellationToken cancellationToken)
    {
        // The run's trace starts here, and its traceparent travels with the job to the workers.
        using var activity = RelayForgeTelemetry.Source.StartActivity("schedule.fire", ActivityKind.Producer);
        activity?.SetTag("relayforge.schedule.name", definition.Name);
        activity?.SetTag("relayforge.schedule.scheduled_for", scheduledFor.ToString("O"));

        var active = await _db.Jobs.AsNoTracking()
            .Where(j => j.DefinitionId == definition.Id && ActiveStates.Contains(j.State))
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        if (active.Count > 0)
        {
            switch (definition.ConcurrencyPolicy)
            {
                case ConcurrencyPolicy.Forbid:
                    _logger.LogInformation("Schedule {Name}: run for {At:u} skipped, the previous run is still active", definition.Name, scheduledFor);
                    return null;

                case ConcurrencyPolicy.Replace:
                    foreach (var jobId in active)
                    {
                        await _operations.CancelAsync(jobId, cancellationToken);
                    }

                    break;
            }
        }

        var template = JsonSerializer.Deserialize<SubmitJobRequest>(definition.Template, HandlerJson.Options)!;
        var request = template with { Name = template.Name ?? $"{definition.Name} {(manual ? "manual run" : $"{scheduledFor:yyyy-MM-dd HH:mm}")}" };

        try
        {
            var result = await _submissions.SubmitAsync(
                request,
                new SubmitContext("schedule", DefinitionId: definition.Id, ScheduledFor: scheduledFor),
                cancellationToken);

            if (result.Created)
            {
                _logger.LogInformation("Schedule {Name} fired for {At:u}: job {JobId}", definition.Name, scheduledFor, result.JobId);
            }

            return result;
        }
        catch (JobValidationException ex)
        {
            // The template stopped being valid (e.g. a handler was removed). Don't retry forever.
            _logger.LogError("Schedule {Name} has an invalid template and was not fired: {Error}", definition.Name, ex.Message);
            return null;
        }
    }

    /// <summary>Creates RelayForge's own schedules if missing: today, the retention job.</summary>
    public async Task EnsureSystemSchedulesAsync(CancellationToken cancellationToken)
    {
        if (await _db.JobDefinitions.AnyAsync(d => d.Name == RetentionScheduleName, cancellationToken))
        {
            return;
        }

        var cron = _configuration["Retention:Cron"] ?? "0 3 * * *";
        var days = _configuration.GetValue<int?>("Retention:Days") ?? 7;
        var template = new SubmitJobRequest(
            "Retention: delete old runs",
            null,
            [
                new SubmitJobTaskRequest(
                    "retention",
                    "delete-old-runs",
                    Type: RetentionHandler.TypeName,
                    Payload: JsonSerializer.SerializeToElement(new { olderThanDays = days, batchSize = 1000 }, HandlerJson.Options),
                    MaxAttempts: 3),
            ]);

        _db.JobDefinitions.Add(new JobDefinition
        {
            Id = Guid.NewGuid(),
            Name = RetentionScheduleName,
            Cron = cron,
            TimeZone = "UTC",
            Template = JsonSerializer.Serialize(template, HandlerJson.Options),
            MisfirePolicy = MisfirePolicy.FireOnce,
            ConcurrencyPolicy = ConcurrencyPolicy.Forbid,
            NextFireAt = ScheduleCalculator.Next(cron, "UTC", DateTimeOffset.UtcNow),
            Enabled = true,
            IsSystem = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another instance seeded it first (unique name).
            _db.ChangeTracker.Clear();
        }
    }

    private static TEnum ParsePolicy<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw Invalid($"Unknown {typeof(TEnum).Name} '{value}'. Use one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }

    private static JobValidationException Invalid(string message) => new("Invalid schedule", message);
}
