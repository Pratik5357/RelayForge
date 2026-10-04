namespace RelayForge.Domain.Dag;

public static class DagValidator
{
    /// <summary>
    /// Validates a submitted task graph: at least one task, no duplicate keys, every
    /// dependsOn reference resolves to a task in the same payload, and no cycles.
    /// Throws ArgumentException for shape problems, DagCycleException for cycles.
    /// </summary>
    public static void Validate(IReadOnlyList<DagNodeInput> nodes)
    {
        if (nodes.Count == 0)
        {
            throw new ArgumentException("A job must have at least one task.");
        }

        var keys = new HashSet<string>();
        foreach (var node in nodes)
        {
            if (!keys.Add(node.Key))
            {
                throw new ArgumentException($"Duplicate task key: '{node.Key}'.");
            }
        }

        foreach (var node in nodes)
        {
            foreach (var dep in node.DependsOn)
            {
                if (!keys.Contains(dep))
                {
                    throw new ArgumentException($"Task '{node.Key}' depends on unknown task '{dep}'.");
                }
            }
        }

        var edges = nodes.ToDictionary(
            n => n.Key,
            IReadOnlyList<string> (n) => n.DependsOn);

        // Throws DagCycleException if a cycle exists; the resulting order is unused here.
        TopologicalSorter.Sort(edges);
    }
}
