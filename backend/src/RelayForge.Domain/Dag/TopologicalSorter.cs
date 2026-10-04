namespace RelayForge.Domain.Dag;

/// <summary>
/// Kahn's-algorithm topological sort, generic over the node-key type so it serves both
/// submission-time validation (string keys from the request payload) and runtime readiness
/// computation (JobTask Guid ids from the database).
/// </summary>
public static class TopologicalSorter
{
    /// <summary>
    /// Returns nodes in a valid topological order (dependencies before dependents).
    /// Throws <see cref="DagCycleException"/> naming the nodes involved in a cycle, if any.
    /// </summary>
    public static IReadOnlyList<TKey> Sort<TKey>(IReadOnlyDictionary<TKey, IReadOnlyList<TKey>> dependsOn)
        where TKey : notnull
    {
        var inDegree = new Dictionary<TKey, int>();
        var dependents = new Dictionary<TKey, List<TKey>>();

        foreach (var key in dependsOn.Keys)
        {
            inDegree[key] = 0;
            dependents[key] = new List<TKey>();
        }

        foreach (var (key, deps) in dependsOn)
        {
            inDegree[key] = deps.Count;
            foreach (var dep in deps)
            {
                dependents[dep].Add(key);
            }
        }

        var queue = new Queue<TKey>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<TKey>();

        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            order.Add(key);

            foreach (var dependent in dependents[key])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        if (order.Count != dependsOn.Count)
        {
            var involved = dependsOn.Keys
                .Except(order)
                .Select(k => k!.ToString()!)
                .ToList();
            throw new DagCycleException(involved);
        }

        return order;
    }
}
