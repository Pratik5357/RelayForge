namespace TaskScheduler.Domain;

public sealed record DagNode(string Key, IReadOnlyList<string> DependsOn);

/// <summary>
/// Kahn's algorithm: produces a valid execution order or detects a cycle.
/// Independent tasks stay unordered relative to each other (stable by input order).
/// </summary>
public static class DagGraph
{
    public static IReadOnlyList<string> TopologicalSort(IReadOnlyList<DagNode> nodes)
    {
        var keys = nodes.Select(n => n.Key).ToList();
        if (keys.Count != keys.Distinct(StringComparer.Ordinal).Count())
        {
            throw new ArgumentException("Task keys must be unique within a job.");
        }

        var keySet = keys.ToHashSet(StringComparer.Ordinal);
        var incoming = keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        var outgoing = keys.ToDictionary(k => k, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            foreach (var dep in node.DependsOn)
            {
                if (!keySet.Contains(dep))
                {
                    throw new ArgumentException($"Task '{node.Key}' depends on unknown task '{dep}'.");
                }

                if (string.Equals(dep, node.Key, StringComparison.Ordinal))
                {
                    throw new DagCycleException($"Task '{node.Key}' cannot depend on itself.");
                }

                incoming[node.Key]++;
                outgoing[dep].Add(node.Key);
            }
        }

        var ready = new Queue<string>(keys.Where(k => incoming[k] == 0));
        var ordered = new List<string>(keys.Count);

        while (ready.Count > 0)
        {
            var current = ready.Dequeue();
            ordered.Add(current);

            foreach (var next in outgoing[current])
            {
                incoming[next]--;
                if (incoming[next] == 0)
                {
                    ready.Enqueue(next);
                }
            }
        }

        if (ordered.Count != keys.Count)
        {
            var cyclic = keys.Except(ordered).ToList();
            throw new DagCycleException(
                $"Cycle detected involving tasks: {string.Join(", ", cyclic)}.");
        }

        return ordered;
    }

    public static IReadOnlyList<string> ReadyKeys(
        IReadOnlyList<DagNode> nodes,
        IReadOnlySet<string> succeededKeys)
    {
        return nodes
            .Where(n => !succeededKeys.Contains(n.Key))
            .Where(n => n.DependsOn.All(succeededKeys.Contains))
            .Select(n => n.Key)
            .ToList();
    }
}
