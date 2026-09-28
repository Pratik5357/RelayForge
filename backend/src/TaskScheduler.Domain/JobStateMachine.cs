namespace TaskScheduler.Domain;

public static class JobStateMachine
{
    public static JobState FromTasks(IReadOnlyCollection<JobTask> tasks, bool cancelRequested)
    {
        if (tasks.Count == 0)
        {
            return JobState.Succeeded;
        }

        if (cancelRequested && tasks.All(t => t.State is TaskState.Cancelled or TaskState.Succeeded))
        {
            return JobState.Cancelled;
        }

        if (tasks.Any(t => t.State is TaskState.Running or TaskState.Pending))
        {
            return tasks.Any(t => t.State is TaskState.Succeeded or TaskState.Failed or TaskState.DeadLettered)
                ? JobState.Running
                : JobState.Pending;
        }

        var anyDead = tasks.Any(t => t.State is TaskState.DeadLettered or TaskState.Failed);
        var anySuccess = tasks.Any(t => t.State == TaskState.Succeeded);

        if (anyDead && anySuccess)
        {
            return JobState.PartiallyFailed;
        }

        if (anyDead)
        {
            return JobState.Failed;
        }

        if (cancelRequested)
        {
            return JobState.Cancelled;
        }

        return JobState.Succeeded;
    }
}
