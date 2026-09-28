using TaskScheduler.Domain;

namespace TaskScheduler.UnitTests;

public class DagGraphTests
{
    [Fact]
    public void TopologicalSort_LinearChain_PreservesDependencyOrder()
    {
        var nodes = new[]
        {
            new DagNode("extract", []),
            new DagNode("transform", ["extract"]),
            new DagNode("load", ["transform"])
        };

        var order = DagGraph.TopologicalSort(nodes);

        Assert.Equal(["extract", "transform", "load"], order);
    }

    [Fact]
    public void TopologicalSort_IndependentRoots_BothAppearBeforeDependents()
    {
        var nodes = new[]
        {
            new DagNode("a", []),
            new DagNode("b", []),
            new DagNode("c", ["a", "b"])
        };

        var order = DagGraph.TopologicalSort(nodes).ToList();
        Assert.Equal(3, order.Count);
        Assert.True(order.IndexOf("a") < order.IndexOf("c"));
        Assert.True(order.IndexOf("b") < order.IndexOf("c"));
    }

    [Fact]
    public void TopologicalSort_Cycle_Throws()
    {
        var nodes = new[]
        {
            new DagNode("a", ["b"]),
            new DagNode("b", ["a"])
        };

        Assert.Throws<DagCycleException>(() => DagGraph.TopologicalSort(nodes));
    }

    [Fact]
    public void TopologicalSort_DuplicateKeys_Throws()
    {
        var nodes = new[]
        {
            new DagNode("a", []),
            new DagNode("a", [])
        };

        Assert.Throws<ArgumentException>(() => DagGraph.TopologicalSort(nodes));
    }

    [Fact]
    public void ReadyKeys_WaitsForDependencies()
    {
        var nodes = new[]
        {
            new DagNode("a", []),
            new DagNode("b", ["a"])
        };

        Assert.Equal(["a"], DagGraph.ReadyKeys(nodes, new HashSet<string>()));
        Assert.Equal(["b"], DagGraph.ReadyKeys(nodes, new HashSet<string> { "a" }));
    }
}
