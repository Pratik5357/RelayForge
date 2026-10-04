namespace RelayForge.Domain.Dag;

/// <summary>
/// Reactive readiness: a node is "ready" once every node it depends on has succeeded.
/// Deliberately not level-based — siblings in the same "level" can finish at different
/// times, so a job's wall-clock time is genuinely max(slowest branch), not bound to a
/// synchronized batch boundary.
/// </summary>
public static class DagReadiness
{
    public static IReadOnlyList<TKey> GetReadyKeys<TKey>(
        IReadOnlyDictionary<TKey, IReadOnlyList<TKey>> dependsOn,
        Func<TKey, bool> isPending,
        Func<TKey, bool> isSucceeded)
        where TKey : notnull
    {
        var ready = new List<TKey>();

        foreach (var (key, deps) in dependsOn)
        {
            if (!isPending(key))
            {
                continue;
            }

            if (deps.All(isSucceeded))
            {
                ready.Add(key);
            }
        }

        return ready;
    }
}
