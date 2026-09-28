# ADR 0001: RabbitMQ over Kafka

## Status
Accepted (Phase 3)

## Context
The engine needs a durable work queue so API processes can enqueue ready tasks and multiple workers can consume them. Kafka and RabbitMQ are the usual candidates.

## Decision
Use RabbitMQ with MassTransit.

## Consequences
- At-least-once delivery matches lease + idempotency keys already in the domain.
- Operationally lighter than Kafka for a portfolio compose stack.
- Throughput is lower than a log-based bus; acceptable for this project's scale.
- Competing consumers on a single queue (`relayforge-task-ready`) give horizontal worker scale without partition math.
