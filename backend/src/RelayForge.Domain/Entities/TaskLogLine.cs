namespace RelayForge.Domain.Entities;

/// <summary>One line a task attempt wrote. Capped per attempt so a noisy task can't fill the database.</summary>
public class TaskLogLine
{
    public long Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid JobId { get; set; }
    public int Attempt { get; set; }
    public int Sequence { get; set; }
    public string Stream { get; set; } = "stdout";
    public string Line { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
}
