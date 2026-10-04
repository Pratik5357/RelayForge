namespace RelayForge.Domain.Dag;

public record DagNodeInput(string Key, IReadOnlyList<string> DependsOn);
