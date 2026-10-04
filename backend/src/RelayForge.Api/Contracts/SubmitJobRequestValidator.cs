using RelayForge.Domain.Dag;

namespace RelayForge.Api.Contracts;

public static class SubmitJobRequestValidator
{
    /// <summary>
    /// Validates request shape and DAG structure. Throws ArgumentException for shape
    /// problems and DagCycleException for cycles — both are translated to 400 responses
    /// by the endpoint, with a message naming the offending task(s).
    /// </summary>
    public static void Validate(SubmitJobRequest request)
    {
        if (request.Tasks is null || request.Tasks.Count == 0)
        {
            throw new ArgumentException("A job must have at least one task.");
        }

        foreach (var task in request.Tasks)
        {
            if (string.IsNullOrWhiteSpace(task.Key))
            {
                throw new ArgumentException("Every task must have a non-empty key.");
            }

            if (string.IsNullOrWhiteSpace(task.Name))
            {
                throw new ArgumentException($"Task '{task.Key}' must have a non-empty name.");
            }

            if (task.SimulatedDurationMs < 0)
            {
                throw new ArgumentException($"Task '{task.Key}' has a negative simulatedDurationMs.");
            }

            if (task.MaxAttempts is < 1)
            {
                throw new ArgumentException($"Task '{task.Key}' has maxAttempts less than 1.");
            }

            if (task.FailUntilAttempt is < 1)
            {
                throw new ArgumentException($"Task '{task.Key}' has failUntilAttempt less than 1.");
            }
        }

        var nodes = request.Tasks
            .Select(t => new DagNodeInput(t.Key, t.DependsOn ?? Array.Empty<string>()))
            .ToList();

        DagValidator.Validate(nodes);
    }
}
