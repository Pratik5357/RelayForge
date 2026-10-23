namespace RelayForge.Domain.Enums;

public enum TaskIsolation
{
    /// <summary>On a worker thread. Cheapest; a misbehaving handler can't be force-stopped.</summary>
    InProcess,

    /// <summary>In a RelayForge.Runner child process, killed on timeout or cancel.</summary>
    Process,

    /// <summary>In a throwaway Docker container with memory, CPU and network limits.</summary>
    Container,
}
