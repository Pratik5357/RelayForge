namespace RelayForge.Domain.Dag;

public class DagCycleException : Exception
{
    public IReadOnlyList<string> InvolvedKeys { get; }

    public DagCycleException(IReadOnlyList<string> involvedKeys)
        : base($"Cycle detected among tasks: {string.Join(", ", involvedKeys)}")
    {
        InvolvedKeys = involvedKeys;
    }
}
