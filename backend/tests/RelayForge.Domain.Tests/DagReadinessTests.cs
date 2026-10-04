using RelayForge.Domain.Dag;

namespace RelayForge.Domain.Tests;

public class DagReadinessTests
{
    [Fact]
    public void GetReadyKeys_TaskWithNoDependencies_IsReadyImmediately()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = Array.Empty<string>(),
        };

        var ready = DagReadiness.GetReadyKeys(
            edges,
            isPending: _ => true,
            isSucceeded: _ => false);

        Assert.Equal(new[] { "A" }, ready);
    }

    [Fact]
    public void GetReadyKeys_DependencyNotYetSucceeded_IsNotReady()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["A"] = Array.Empty<string>(),
            ["B"] = new[] { "A" },
        };

        var ready = DagReadiness.GetReadyKeys(
            edges,
            isPending: key => key == "B",
            isSucceeded: key => false);

        Assert.Empty(ready);
    }

    [Fact]
    public void GetReadyKeys_DiamondSiblings_BothReadyOnceSharedDependencySucceeds()
    {
        var edges = new Dictionary<string, IReadOnlyList<string>>
        {
            ["D"] = Array.Empty<string>(),
            ["E"] = new[] { "D" },
            ["F"] = new[] { "D" },
        };

        var ready = DagReadiness.GetReadyKeys(
            edges,
            isPending: key => key is "E" or "F",
            isSucceeded: key => key == "D");

        Assert.Equal(new HashSet<string> { "E", "F" }, ready.ToHashSet());
    }
}
