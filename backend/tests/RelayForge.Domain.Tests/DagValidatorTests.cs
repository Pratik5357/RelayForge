using RelayForge.Domain.Dag;

namespace RelayForge.Domain.Tests;

public class DagValidatorTests
{
    [Fact]
    public void Validate_SimpleChain_DoesNotThrow()
    {
        var nodes = new List<DagNodeInput>
        {
            new("A", Array.Empty<string>()),
            new("B", new[] { "A" }),
            new("C", new[] { "B" }),
        };

        var ex = Record.Exception(() => DagValidator.Validate(nodes));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_Diamond_DoesNotThrow()
    {
        var nodes = new List<DagNodeInput>
        {
            new("D", Array.Empty<string>()),
            new("E", new[] { "D" }),
            new("F", new[] { "D" }),
        };

        var ex = Record.Exception(() => DagValidator.Validate(nodes));

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_EmptyTaskList_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => DagValidator.Validate(new List<DagNodeInput>()));
    }

    [Fact]
    public void Validate_DuplicateKey_ThrowsArgumentException()
    {
        var nodes = new List<DagNodeInput>
        {
            new("A", Array.Empty<string>()),
            new("A", Array.Empty<string>()),
        };

        Assert.Throws<ArgumentException>(() => DagValidator.Validate(nodes));
    }

    [Fact]
    public void Validate_UnknownDependency_ThrowsArgumentException()
    {
        var nodes = new List<DagNodeInput>
        {
            new("A", new[] { "ghost" }),
        };

        Assert.Throws<ArgumentException>(() => DagValidator.Validate(nodes));
    }

    [Fact]
    public void Validate_Cycle_ThrowsDagCycleException()
    {
        var nodes = new List<DagNodeInput>
        {
            new("A", new[] { "B" }),
            new("B", new[] { "A" }),
        };

        Assert.Throws<DagCycleException>(() => DagValidator.Validate(nodes));
    }
}
