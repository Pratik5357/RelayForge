namespace TaskScheduler.Domain;

public sealed class DagCycleException : Exception
{
    public DagCycleException(string message) : base(message)
    {
    }
}
