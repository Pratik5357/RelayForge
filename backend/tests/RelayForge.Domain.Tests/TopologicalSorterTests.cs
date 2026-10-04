using RelayForge.Domain.Dag;

namespace RelayForge.Domain.Tests;

public class TopologicalSorterTests
{
    [Fact]
    public void Sort_SimpleChain_OrdersDependenciesBeforeDependents()
    {
        // A -> B -> C
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = Array.Empty<string>(),
            ["B"] = new[] { "A" },
            ["C"] = new[] { "B" },
        };

        var order = TopologicalSorter.Sort(edges);

        Assert.Equal(new[] { "A", "B", "C" }, order);
    }

    [Fact]
    public void Sort_Diamond_KeepsSharedDependencyBeforeBothBranches()
    {
        // D -> E, D -> F (E and F independent of each other)
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["D"] = Array.Empty<string>(),
            ["E"] = new[] { "D" },
            ["F"] = new[] { "D" },
        };

        var order = TopologicalSorter.Sort(edges).ToList();

        Assert.Equal("D", order[0]);
        Assert.Contains("E", order);
        Assert.Contains("F", order);
        Assert.True(order.IndexOf("D") < order.IndexOf("E"));
        Assert.True(order.IndexOf("D") < order.IndexOf("F"));
    }

    [Fact]
    public void Sort_Cycle_ThrowsDagCycleExceptionNamingInvolvedNodes()
    {
        // A -> B -> A
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = new[] { "B" },
            ["B"] = new[] { "A" },
        };

        var ex = Assert.Throws<DagCycleException>(() => TopologicalSorter.Sort(edges));

        Assert.Contains("A", ex.InvolvedKeys);
        Assert.Contains("B", ex.InvolvedKeys);
    }
}
