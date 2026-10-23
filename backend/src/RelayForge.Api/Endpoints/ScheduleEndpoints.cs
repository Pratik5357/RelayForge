using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RelayForge.Api.Contracts;
using RelayForge.Domain.Entities;
using RelayForge.Handlers;
using RelayForge.Infrastructure;
using RelayForge.Infrastructure.Jobs;
using RelayForge.Infrastructure.Scheduling;

namespace RelayForge.Api.Endpoints;

public static class ScheduleEndpoints
{
    public static RouteGroupBuilder MapScheduleEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPost("/{id:guid}/enable", (Guid id, ScheduleService s, RelayForgeDbContext db, CancellationToken ct) => SetEnabledAsync(id, true, s, db, ct));
        group.MapPost("/{id:guid}/disable", (Guid id, ScheduleService s, RelayForgeDbContext db, CancellationToken ct) => SetEnabledAsync(id, false, s, db, ct));
        group.MapPost("/{id:guid}/trigger", TriggerAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(RelayForgeDbContext db, CancellationToken cancellationToken)
    {
        var definitions = await db.JobDefinitions.AsNoTracking()
            .OrderByDescending(d => d.IsSystem).ThenBy(d => d.Name)
            .ToListAsync(cancellationToken);

        var responses = new List<ScheduleResponse>();

        foreach (var definition in definitions)
        {
            responses.Add(await ToResponseAsync(definition, db, cancellationToken));
        }

        return Results.Ok(responses);
    }

    private static async Task<IResult> GetAsync(Guid id, RelayForgeDbContext db, CancellationToken cancellationToken)
    {
        var definition = await db.JobDefinitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return definition is null ? Results.NotFound() : Results.Ok(await ToResponseAsync(definition, db, cancellationToken));
    }

    private static async Task<IResult> CreateAsync(CreateScheduleRequest request, ScheduleService schedules, RelayForgeDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            var definition = await schedules.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/schedules/{definition.Id}", await ToResponseAsync(definition, db, cancellationToken));
        }
        catch (JobValidationException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: ex.Title);
        }
    }

    private static async Task<IResult> SetEnabledAsync(Guid id, bool enabled, ScheduleService schedules, RelayForgeDbContext db, CancellationToken cancellationToken)
    {
        var definition = await schedules.SetEnabledAsync(id, enabled, cancellationToken);
        return definition is null ? Results.NotFound() : Results.Ok(await ToResponseAsync(definition, db, cancellationToken));
    }

    private static async Task<IResult> TriggerAsync(Guid id, ScheduleService schedules, RelayForgeDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.JobDefinitions.AnyAsync(d => d.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var result = await schedules.TriggerAsync(id, cancellationToken);

        return result is null
            ? Results.Problem(detail: "Not run: the previous run is still active and this schedule forbids overlap, or its template is no longer valid.", statusCode: StatusCodes.Status409Conflict, title: "Not run")
            : Results.Ok(await JobEndpoints.LoadJobDetailAsync(db, result.JobId, cancellationToken));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ScheduleService schedules, CancellationToken cancellationToken) =>
        await schedules.DeleteAsync(id, cancellationToken) switch
        {
            null => Results.NotFound(),
            false => Results.Problem(detail: "RelayForge's own schedules can be disabled, not deleted.", statusCode: StatusCodes.Status409Conflict, title: "System schedule"),
            true => Results.NoContent(),
        };

    private static async Task<ScheduleResponse> ToResponseAsync(JobDefinition d, RelayForgeDbContext db, CancellationToken cancellationToken)
    {
        var runs = await db.Jobs.AsNoTracking()
            .Where(j => j.DefinitionId == d.Id)
            .OrderByDescending(j => j.CreatedAt)
            .Take(5)
            .Select(JobEndpoints.SummaryProjection)
            .ToListAsync(cancellationToken);

        return new ScheduleResponse(
            d.Id,
            d.Name,
            d.Cron,
            d.TimeZone,
            d.MisfirePolicy.ToString(),
            d.ConcurrencyPolicy.ToString(),
            d.Enabled,
            d.IsSystem,
            d.NextFireAt,
            d.LastFiredAt,
            JsonDocument.Parse(d.Template).RootElement,
            runs);
    }

    public static RouteGroupBuilder MapHandlerEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (IEnumerable<ITaskHandler> handlers) =>
            handlers.OrderBy(h => h.Type).Select(h => new HandlerResponse(h.Type, h.InProcessOnly)));
        return group;
    }
}
