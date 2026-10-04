namespace RelayForge.Infrastructure.Execution;

public class ReliabilityOptions
{
    public int BaseBackoffMs { get; set; } = 500;
    public int MaxBackoffMs { get; set; } = 8000;
    public int DefaultMaxAttempts { get; set; } = 4;
    public int LeaseBufferMs { get; set; } = 15000;
    public int SweepIntervalMs { get; set; } = 1000;
}
