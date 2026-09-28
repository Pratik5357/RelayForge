namespace TaskScheduler.Infrastructure.Queueing;

public sealed record TaskReadyMessage(Guid TaskId);
