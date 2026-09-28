using TaskScheduler.Domain;

namespace TaskScheduler.UnitTests;

public class JobStateMachineTests
{
    [Fact]
    public void AllSucceeded_IsSucceeded()
    {
        var tasks = new[]
        {
            new JobTask { State = TaskState.Succeeded },
            new JobTask { State = TaskState.Succeeded }
        };

        Assert.Equal(JobState.Succeeded, JobStateMachine.FromTasks(tasks, cancelRequested: false));
    }

    [Fact]
    public void MixOfSuccessAndDeadLetter_IsPartiallyFailed()
    {
        var tasks = new[]
        {
            new JobTask { State = TaskState.Succeeded },
            new JobTask { State = TaskState.DeadLettered }
        };

        Assert.Equal(JobState.PartiallyFailed, JobStateMachine.FromTasks(tasks, false));
    }

    [Fact]
    public void PendingAndSucceeded_IsRunning()
    {
        var tasks = new[]
        {
            new JobTask { State = TaskState.Succeeded },
            new JobTask { State = TaskState.Pending }
        };

        Assert.Equal(JobState.Running, JobStateMachine.FromTasks(tasks, false));
    }
}
