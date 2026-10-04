namespace RelayForge.Domain.Entities;

// A DAG edge: JobTaskId "depends on" DependsOnJobTaskId.
public class TaskDependency
{
    public Guid Id { get; set; }

    public Guid JobTaskId { get; set; }
    public JobTask? JobTask { get; set; }

    public Guid DependsOnJobTaskId { get; set; }
    public JobTask? DependsOnJobTask { get; set; }
}
