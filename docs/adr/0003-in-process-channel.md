# ADR 0003: In-process channel before the broker

## Status
Accepted (Phase 1)

## Context
The spec requires a working engine before introducing RabbitMQ. Jumping to a broker first hides bugs in DAG scheduling and retries.

## Decision
`ITaskQueue` has two implementations: `ChannelTaskQueue` (bounded `Channel<Guid>`) and `MassTransitTaskQueue`. Phase 1/2 and tests use the channel. Compose uses RabbitMQ.

## Consequences
- Local `dotnet run` on the API is enough to demo a DAG.
- Workers in a separate process only matter once `Scheduler:Queue` is `RabbitMq`.
