namespace TaskScheduler.Domain;

public sealed class TaskDependency
{
    public Guid JobTaskId { get; set; }
    public JobTask JobTask { get; set; } = null!;

    public Guid DependsOnTaskId { get; set; }
    public JobTask DependsOnTask { get; set; } = null!;
}
