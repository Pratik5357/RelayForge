using Microsoft.Extensions.DependencyInjection;
using RelayForge.Handlers.BuiltIn;

namespace RelayForge.Handlers;

/// <summary>Maps a task Type to its handler. Built from every registered <see cref="ITaskHandler"/>.</summary>
public sealed class TaskHandlerRegistry
{
    private readonly Dictionary<string, ITaskHandler> _handlers;

    public TaskHandlerRegistry(IEnumerable<ITaskHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.Type, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> Types => _handlers.Keys;

    public ITaskHandler? Find(string type) => _handlers.GetValueOrDefault(type);
}

public static class HandlerServiceCollectionExtensions
{
    public static IServiceCollection AddTaskHandler<THandler>(this IServiceCollection services)
        where THandler : class, ITaskHandler =>
        services.AddSingleton<ITaskHandler, THandler>();

    /// <summary>The handlers that can run anywhere, worker or runner.</summary>
    public static IServiceCollection AddBuiltInTaskHandlers(this IServiceCollection services)
    {
        services.AddHttpClient(HttpRequestHandler.ClientName);
        services.AddTaskHandler<SimulatedHandler>();
        services.AddTaskHandler<DelayHandler>();
        services.AddTaskHandler<HttpRequestHandler>();
        services.AddSingleton<TaskHandlerRegistry>();
        return services;
    }
}
